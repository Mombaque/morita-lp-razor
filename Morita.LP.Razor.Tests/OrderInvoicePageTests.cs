using System;
using System.Collections.Generic;
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

public sealed class OrderInvoicePageTests
{
    private const string OrderNumber = "MF-BBBBBBBBBBBBBBBB";

    [Fact]
    public async Task Order_page_lists_the_invoice_and_proxies_its_files()
    {
        var orders = new InvoiceOrders(new PublicOrderInvoice { Number = 77, Series = 1, AuthorizedAt = DateTimeOffset.UtcNow, Files = ["danfe", "xml"] });
        using var factory = Create(orders);
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await (await client.GetAsync($"/order/{OrderNumber}")).Content.ReadAsStringAsync());
        Assert.Contains("NF-e nº 77 · série 1", html);
        Assert.Contains($"href=\"/order/{OrderNumber}?handler=Invoice&kind=danfe\"", html);
        Assert.Contains($"href=\"/order/{OrderNumber}?handler=Invoice&kind=xml\"", html);

        var pdf = await client.GetAsync($"/order/{OrderNumber}?handler=Invoice&kind=danfe");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal("danfe", orders.LastKind);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/order/{OrderNumber}?handler=Invoice&kind=zip")).StatusCode);
    }

    [Fact]
    public async Task Cancelled_invoice_is_announced_without_download_links()
    {
        using var factory = Create(new InvoiceOrders(new PublicOrderInvoice { Number = 77, Cancelled = true }));
        var html = await (await factory.CreateClient().GetAsync($"/order/{OrderNumber}")).Content.ReadAsStringAsync();

        Assert.Contains("Esta nota fiscal foi cancelada.", html);
        Assert.DoesNotContain("data-invoice-file", html);
    }

    [Fact]
    public async Task Order_without_invoice_shows_no_invoice_section()
    {
        using var factory = Create(new InvoiceOrders(null));
        var html = await (await factory.CreateClient().GetAsync($"/order/{OrderNumber}")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("data-order-invoice", html);
    }

    private static WebApplicationFactory<Program> Create(InvoiceOrders orders) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOrderClient>();
                services.RemoveAll<IOrderAccessCookieStore>();
                services.AddScoped<IOrderClient>(_ => orders);
                services.AddScoped<IOrderAccessCookieStore>(_ => new Access());
            });
        });

    private sealed class InvoiceOrders(PublicOrderInvoice? invoice) : IOrderClient
    {
        public string? LastKind { get; private set; }

        public Task<OrderResult> GetAsync(string publicOrderNumber, string accessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrderResult(OrderLoadState.Success, new PublicOrder
            {
                PublicOrderNumber = OrderNumber,
                PaymentStatus = "converted",
                FulfillmentStatus = "pending",
                FulfillmentMethod = "pickup",
                Amount = 100,
                Currency = "BRL",
                CreatedAt = DateTimeOffset.UtcNow,
                PickupDisplayName = "Loja",
                Lines = [new PublicOrderLine { Description = "Kimono", Presentation = "Kimono · Azul · A2", Quantity = 1, UnitPrice = 100, Total = 100 }],
                Invoice = invoice
            }));

        public Task<InvoiceFile?> DownloadInvoiceAsync(string publicOrderNumber, string accessToken, string kind, CancellationToken cancellationToken = default)
        {
            LastKind = kind;
            return Task.FromResult<InvoiceFile?>(new InvoiceFile([1, 2, 3], kind == "danfe" ? "application/pdf" : "application/xml", $"nfe.{(kind == "danfe" ? "pdf" : "xml")}"));
        }
    }

    private sealed class Access : IOrderAccessCookieStore
    {
        public OrderAccess? Read(string publicOrderNumber) => new(OrderNumber, new string('o', 32), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        public bool Write(string publicOrderNumber, string token) => true;
        public void Clear() { }
    }
}
