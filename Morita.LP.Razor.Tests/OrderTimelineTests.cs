using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class OrderTimelineTests
{
    private const string OrderNumber = "MF-CCCCCCCCCCCCCCCC";

    [Fact]
    public void Shipping_order_in_transit_marks_previous_steps_done_and_uses_the_shipped_time()
    {
        var shippedAt = DateTimeOffset.UtcNow.AddHours(-3);
        var order = Order("shipping", "InTransit", new PublicShipment { Status = "InTransit", ShippedAt = shippedAt });

        var steps = OrderTimeline.Build(order);

        Assert.Equal(["Pedido confirmado", "Preparando envio", "Etiqueta emitida", "Em trânsito", "Entregue"], steps.Select(step => step.Label));
        Assert.Equal(
            [OrderTimelineStepState.Done, OrderTimelineStepState.Done, OrderTimelineStepState.Done, OrderTimelineStepState.Current, OrderTimelineStepState.Upcoming],
            steps.Select(step => step.State));
        Assert.Equal(shippedAt, steps[3].At);
        Assert.Null(steps[4].At);
    }

    [Fact]
    public void Delivered_order_marks_every_step_done()
    {
        var order = Order("shipping", "Delivered", new PublicShipment { Status = "Delivered", DeliveredAt = DateTimeOffset.UtcNow });

        Assert.All(OrderTimeline.Build(order), step => Assert.Equal(OrderTimelineStepState.Done, step.State));
    }

    [Fact]
    public void Shipping_order_follows_the_shipment_when_the_order_status_stays_pending()
    {
        var order = Order("shipping", "pending", new PublicShipment { Status = "delivered", DeliveredAt = DateTimeOffset.UtcNow });

        Assert.Equal("delivered", AccountPresentation.EffectiveFulfillmentStatus(order));
        Assert.All(OrderTimeline.Build(order), step => Assert.Equal(OrderTimelineStepState.Done, step.State));
    }

    [Theory]
    [InlineData("shipping", "pending", "intransit", "intransit")]
    [InlineData("shipping", "cancelled", "delivered", "cancelled")]
    [InlineData("shipping", "pending", null, "pending")]
    [InlineData("pickup", "readyforpickup", null, "readyforpickup")]
    public void Effective_status_prefers_the_shipment_only_for_active_shipping_orders(string method, string fulfillment, string? shipment, string expected)
    {
        Assert.Equal(expected, AccountPresentation.EffectiveFulfillmentStatus(method, fulfillment, shipment));
    }

    [Fact]
    public async Task Delivered_shipping_order_page_says_delivered_even_when_the_order_status_is_pending()
    {
        var order = Order("shipping", "pending", new PublicShipment { Status = "delivered", CarrierName = "Correios", ServiceName = "PAC", UpdatedAt = DateTimeOffset.UtcNow, DeliveredAt = DateTimeOffset.UtcNow });
        using var factory = Create(order);

        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync($"/order/{OrderNumber}")).Content.ReadAsStringAsync());

        Assert.Contains("<h1>Pedido entregue</h1>", html);
        Assert.Contains("Status: <strong>Entregue</strong>", html);
        Assert.DoesNotContain("Aguardando preparação", html);
    }

    [Fact]
    public async Task Delivered_shipping_order_page_uses_the_shipment_times_instead_of_the_order_update_time()
    {
        var confirmedAt = new DateTimeOffset(2026, 10, 13, 18, 57, 0, TimeSpan.Zero);
        var deliveredAt = new DateTimeOffset(2026, 10, 13, 19, 26, 0, TimeSpan.Zero);
        var shipment = new PublicShipment { Status = "Delivered", CarrierName = "Sandbox", ServiceName = "1", UpdatedAt = deliveredAt, ShippedAt = deliveredAt, DeliveredAt = deliveredAt };
        using var factory = Create(Order("shipping", "pending", shipment, confirmedAt));

        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync($"/order/{OrderNumber}")).Content.ReadAsStringAsync());

        Assert.Contains("Entregue em <time", html);
        Assert.DoesNotContain("Entrega atualizada em", html);
    }

    [Fact]
    public async Task Pickup_order_page_shows_the_order_update_time()
    {
        var readyAt = new DateTimeOffset(2026, 10, 13, 18, 57, 0, TimeSpan.Zero);
        using var factory = Create(Order("pickup", "ReadyForPickup", fulfillmentUpdatedAt: readyAt));

        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync($"/order/{OrderNumber}")).Content.ReadAsStringAsync());

        Assert.Contains("Entrega atualizada em <time datetime=\"2026-10-13T18:57:00.0000000+00:00\">13/10/2026 15:57</time>", html);
    }

    [Fact]
    public void Pickup_order_ready_for_pickup_is_the_current_step()
    {
        var steps = OrderTimeline.Build(Order("pickup", "ReadyForPickup"));

        Assert.Equal(4, steps.Count);
        Assert.Equal("Pronto para retirada", steps.Single(step => step.State == OrderTimelineStepState.Current).Label);
    }

    [Theory]
    [InlineData("pending", "Pending")]
    [InlineData("refunded", "Cancelled")]
    [InlineData("converted", "Cancelled")]
    [InlineData("converted", "CancellationPending")]
    public void Unpaid_or_cancelled_orders_have_no_timeline(string payment, string fulfillment)
    {
        var order = Order("shipping", fulfillment);
        order = new PublicOrder { PublicOrderNumber = order.PublicOrderNumber, PaymentStatus = payment, FulfillmentStatus = fulfillment, FulfillmentMethod = "shipping" };

        Assert.Empty(OrderTimeline.Build(order));
    }

    [Fact]
    public async Task Order_page_renders_the_timeline_tracking_code_and_carrier_alert()
    {
        var order = Order("shipping", "Exception", new PublicShipment { Status = "Exception", CarrierName = "Correios", ServiceName = "PAC", TrackingCode = "AB123456789BR", UpdatedAt = DateTimeOffset.UtcNow });
        using var factory = Create(order);

        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync($"/order/{OrderNumber}")).Content.ReadAsStringAsync());

        Assert.Contains("data-order-timeline", html);
        Assert.Contains("data-step-state=\"current\" aria-current=\"step\"", html);
        Assert.Contains("data-order-timeline-alert", html);
        Assert.Contains("data-copy-text=\"AB123456789BR\"", html);
        Assert.Contains($"href=\"{OrderTimeline.TrackingSiteUrl}\"", html);
    }

    [Fact]
    public async Task Unpaid_order_page_has_no_timeline()
    {
        using var factory = Create(new PublicOrder { PublicOrderNumber = OrderNumber, PaymentStatus = "pending", FulfillmentStatus = "Pending", FulfillmentMethod = "pickup", Currency = "BRL" });

        var html = await (await factory.CreateClient().GetAsync($"/order/{OrderNumber}")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("data-order-timeline", html);
    }

    private static PublicOrder Order(string method, string fulfillment, PublicShipment? shipment = null, DateTimeOffset? fulfillmentUpdatedAt = null) => new()
    {
        PublicOrderNumber = OrderNumber,
        PaymentStatus = "converted",
        FulfillmentStatus = fulfillment,
        FulfillmentMethod = method,
        Amount = 100,
        Currency = "BRL",
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
        FulfillmentUpdatedAt = fulfillmentUpdatedAt,
        Shipment = shipment,
        Lines = [new PublicOrderLine { Description = "Kimono", Presentation = "Kimono · Azul · A2", Quantity = 1, UnitPrice = 100, Total = 100 }]
    };

    private static WebApplicationFactory<Program> Create(PublicOrder order) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOrderClient>();
                services.RemoveAll<IOrderAccessCookieStore>();
                services.AddScoped<IOrderClient>(_ => new Orders(order));
                services.AddScoped<IOrderAccessCookieStore>(_ => new Access());
            });
        });

    private sealed class Orders(PublicOrder order) : IOrderClient
    {
        public Task<OrderResult> GetAsync(string publicOrderNumber, string accessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrderResult(OrderLoadState.Success, order));
    }

    private sealed class Access : IOrderAccessCookieStore
    {
        public OrderAccess? Read(string publicOrderNumber) => new(OrderNumber, new string('o', 32), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        public bool Write(string publicOrderNumber, string token) => true;
        public void Clear() { }
    }
}
