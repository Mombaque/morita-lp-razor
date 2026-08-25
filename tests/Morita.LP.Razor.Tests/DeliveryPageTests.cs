using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Tests;

public sealed class DeliveryPageTests
{
    [Fact]
    public async Task Active_delivery_renders_safe_content_and_private_headers()
    {
        using var factory = new DeliveryWebFactory();
        factory.Delivery = new PublicDelivery
        {
            Status = "in_transit",
            DisplayOrderNumber = "PED-123",
            CustomerName = "Ana Maria Silva",
            City = "Sorocaba",
            District = "Centro",
            DestinationCity = "Sorocaba",
            DestinationDistrict = "Centro",
            EstimatedDeliveryFrom = DateTimeOffset.Parse("2026-08-25T14:00:00Z"),
            EstimatedDeliveryTo = DateTimeOffset.Parse("2026-08-25T18:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-08-25T08:00:00Z"),
            StatusUpdatedAt = DateTimeOffset.Parse("2026-08-25T10:00:00Z"),
            Items = [new PublicDeliveryItem { Name = "Luva de treino", Quantity = 2 }]
        };
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/entrega/abc12345-safe-token");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Centro &#xB7; Sorocaba", html);
        Assert.Contains("Pedido PED-123", html);
        Assert.Contains("×2", html);
        Assert.Contains("noindex,nofollow,noarchive", html);
        Assert.Contains("no-referrer", html);
        var cacheControl = response.Headers.GetValues("Cache-Control").Single();
        Assert.Contains("private", cacheControl);
        Assert.Contains("no-store", cacheControl);
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("noindex, nofollow, noarchive", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.DoesNotContain("window.API_BASE_URL", html);
        Assert.DoesNotContain("googletagmanager", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Delivered_page_does_not_render_blank_review_and_does_not_schedule_refresh()
    {
        using var factory = new DeliveryWebFactory();
        factory.Delivery = new PublicDelivery { Status = "delivered", Timeline = [] };
        using var client = factory.CreateClient();

        var html = await (await client.GetAsync("/entrega/delivered-token-1")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("Avaliar a Morita", html);
        Assert.DoesNotContain("45000", html);
    }

    [Fact]
    public async Task Missing_or_malformed_token_is_safe_and_does_not_call_upstream()
    {
        using var factory = new DeliveryWebFactory();
        factory.Delivery = new PublicDelivery { Status = "delivered" };
        factory.ClientCalls = 0;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/entrega/not-valid!");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Não encontramos essa entrega", html);
        Assert.Equal(0, factory.ClientCalls);
    }
}

public sealed class DeliveryWebFactory : WebApplicationFactory<Program>
{
    public PublicDelivery? Delivery { get; set; }
    public int ClientCalls { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPublicDeliveryClient>();
            services.AddSingleton<IPublicDeliveryClient>(_ => new FakeDeliveryClient(this));
        });
    }

    private sealed class FakeDeliveryClient(DeliveryWebFactory factory) : IPublicDeliveryClient
    {
        public Task<PublicDelivery?> GetAsync(string publicToken, CancellationToken cancellationToken = default)
        {
            factory.ClientCalls++;
            return Task.FromResult(factory.Delivery);
        }
    }
}
