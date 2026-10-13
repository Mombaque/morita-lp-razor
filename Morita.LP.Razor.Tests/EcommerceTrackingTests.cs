using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
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

public sealed class EcommerceTrackingTests
{
    private const string OrderNumber = "MF-AAAAAAAAAAAAAAAA";
    private static readonly Guid Offer = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public async Task Product_page_renders_view_item_with_catalog_data_only()
    {
        using var factory = Create();
        var html = await GetAsync(factory, "/products/kimono");

        var view = Single(html, "view_item");
        var item = view.GetProperty("ecommerce").GetProperty("items")[0];
        Assert.Equal("kimono", item.GetProperty("item_id").GetString());
        Assert.Equal("Kimono Trançado", item.GetProperty("item_name").GetString());
        Assert.Equal("Morita", item.GetProperty("item_brand").GetString());
        Assert.Equal("Kimonos", item.GetProperty("item_category").GetString());
        Assert.Equal(399.9m, item.GetProperty("price").GetDecimal());
        Assert.Equal("BRL", view.GetProperty("ecommerce").GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Catalog_and_cart_render_list_and_cart_events()
    {
        using var factory = Create();

        var list = Single(await GetAsync(factory, "/products"), "view_item_list");
        Assert.Equal("Catálogo", list.GetProperty("ecommerce").GetProperty("item_list_name").GetString());
        Assert.Equal("kimono", list.GetProperty("ecommerce").GetProperty("items")[0].GetProperty("item_id").GetString());

        var cart = Single(await GetAsync(factory, "/cart"), "view_cart");
        var line = cart.GetProperty("ecommerce").GetProperty("items")[0];
        Assert.Equal(799.8m, cart.GetProperty("ecommerce").GetProperty("value").GetDecimal());
        Assert.Equal("Azul / A2", line.GetProperty("item_variant").GetString());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Order_page_renders_purchase_only_right_after_placing_a_paid_order()
    {
        using var factory = Create();

        var placed = await GetAsync(factory, $"/order/{OrderNumber}?placed=true");
        var purchase = Single(placed, "purchase");
        var ecommerce = purchase.GetProperty("ecommerce");
        Assert.Equal(OrderNumber, ecommerce.GetProperty("transaction_id").GetString());
        Assert.Equal(819.8m, ecommerce.GetProperty("value").GetDecimal());
        Assert.Equal(20m, ecommerce.GetProperty("shipping").GetDecimal());
        var purchased = ecommerce.GetProperty("items")[0];
        Assert.Equal("Kimono Trançado", purchased.GetProperty("item_name").GetString());
        Assert.Equal("Azul / A2", purchased.GetProperty("item_variant").GetString());
        Assert.DoesNotContain("ana@example.com", placed);
        Assert.DoesNotContain("Ana Cliente", Events(placed).Single().GetRawText());

        Assert.Empty(Events(await GetAsync(factory, $"/order/{OrderNumber}")));

        using var pending = Create(paymentStatus: "pending");
        Assert.Empty(Events(await GetAsync(pending, $"/order/{OrderNumber}?placed=true")));
    }

    [Fact]
    public async Task Ads_purchase_conversion_label_is_exposed_only_when_configured()
    {
        using var without = Create();
        Assert.DoesNotContain("analytics-ads-purchase-conversion", await GetAsync(without, "/products"));

        using var with = Create(adsPurchaseConversion: "AW-123/abc");
        Assert.Contains("<meta name=\"analytics-ads-purchase-conversion\" content=\"AW-123/abc\">", await GetAsync(with, "/products"));
    }

    private static JsonElement Single(string html, string eventName) =>
        Assert.Single(Events(html), item => item.GetProperty("event").GetString() == eventName);

    private static List<JsonElement> Events(string html) =>
        Regex.Matches(html, "<script type=\"application/json\" data-analytics-event>(.*?)</script>", RegexOptions.Singleline)
            .Select(match => JsonDocument.Parse(match.Groups[1].Value).RootElement.Clone())
            .ToList();

    private static async Task<string> GetAsync(WebApplicationFactory<Program> factory, string path) =>
        await (await factory.CreateClient().GetAsync(path)).Content.ReadAsStringAsync();

    private static Product Kimono() => new()
    {
        Slug = "kimono",
        Nome = "Kimono Trançado",
        Price = 399.9m,
        Currency = "BRL",
        Availability = "available",
        Brand = new CatalogLookup(1, "morita", "Morita"),
        Category = new CatalogLookup(2, "kimonos", "Kimonos"),
        Modality = new CatalogLookup(3, "jiu-jitsu", "Jiu-Jitsu"),
        Variants = [new ProductVariant { ColorLabel = "Azul", Images = ["/images/azul.jpg"], Offers = [new ProductOffer { PublicOfferId = Offer, SizeLabel = "A2", UnitPrice = 399.9m, Currency = "BRL", Availability = "available" }] }]
    };

    private static WebApplicationFactory<Program> Create(string paymentStatus = "converted", string? adsPurchaseConversion = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            if (adsPurchaseConversion is not null)
                builder.UseSetting("Analytics:GoogleAdsPurchaseConversion", adsPurchaseConversion);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICatalogClient>();
                services.RemoveAll<ICartCookieStore>();
                services.RemoveAll<IOrderClient>();
                services.RemoveAll<IOrderAccessCookieStore>();
                services.AddScoped<ICatalogClient>(_ => new StubCatalog(Kimono()));
                services.AddScoped<ICartCookieStore>(_ => new FixedCart());
                services.AddScoped<IOrderClient>(_ => new StubOrders(paymentStatus));
                services.AddScoped<IOrderAccessCookieStore>(_ => new StubOrderAccess());
            });
        });

    private sealed class StubCatalog(Product product) : ICatalogClient
    {
        public Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default) => Task.FromResult(CatalogResult.Empty());
        public Task<CatalogPage> GetCatalogAsync(CatalogQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogPage([product], 1, CatalogQuery.PageSize, 1, 1, CatalogLoadState.Success));
        public Task<ProductDetailResult> GetProductAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(ProductDetailResult.Success(product));
        public Task<CatalogResult> GetRelatedAsync(string slug, int limit = 4, CancellationToken cancellationToken = default) => Task.FromResult(CatalogResult.Empty());
        public Task<CatalogQuoteResult> QuoteAsync(CatalogQuoteRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogQuoteResult.Success("BRL", 799.8m, [new CatalogQuoteLine { PublicOfferId = Offer, Slug = "kimono", Quantity = 2, Presentation = "Kimono Trançado", ColorLabel = "Azul", SizeLabel = "A2", UnitPrice = 399.9m, LinePrice = 799.8m, Currency = "BRL", Availability = "available" }]));
    }

    private sealed class FixedCart : ICartCookieStore
    {
        public CartState Read() => new(DateTimeOffset.UtcNow, [new CartLine(Offer, 2)]);
        public bool Add(Guid offerId, int quantity) => true;
        public bool Update(Guid offerId, int quantity) => true;
        public bool Remove(Guid offerId) => true;
        public bool Replace(IReadOnlyList<CartLine> lines) => true;
        public void Clear() { }
    }

    private sealed class StubOrders(string paymentStatus) : IOrderClient
    {
        public Task<OrderResult> GetAsync(string publicOrderNumber, string accessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrderResult(OrderLoadState.Success, new PublicOrder
            {
                PublicOrderNumber = OrderNumber,
                PaymentStatus = paymentStatus,
                FulfillmentStatus = "Pending",
                FulfillmentMethod = "shipping",
                Amount = 819.8m,
                Currency = "BRL",
                CreatedAt = DateTimeOffset.UtcNow,
                Shipping = new ShippingSnapshot { CarrierName = "Correios", ServiceName = "PAC", Price = 20m, Address = new CheckoutAddress { Recipient = "Ana Cliente", Street = "Rua", Number = "1", Neighborhood = "Centro", City = "Sorocaba", State = "SP", PostalCode = "18000000" } },
                Lines = [new PublicOrderLine { Description = "KIMONO TRANC AZ A2 (interno)", Presentation = "Kimono Trançado · Azul · A2", Quantity = 2, UnitPrice = 399.9m, Total = 799.8m }]
            }));
    }

    private sealed class StubOrderAccess : IOrderAccessCookieStore
    {
        public OrderAccess? Read(string publicOrderNumber) => new(OrderNumber, new string('o', 32), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        public bool Write(string publicOrderNumber, string token) => true;
        public void Clear() { }
    }
}
