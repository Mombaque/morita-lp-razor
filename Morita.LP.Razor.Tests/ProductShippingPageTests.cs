using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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

public sealed class ProductShippingPageTests
{
    private static readonly Guid AvailableOffer = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SoldOutOffer = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public async Task Product_page_renders_the_category_size_guide_and_the_shipping_estimate_form()
    {
        using var factory = Create(new StubCheckout());
        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync("/products/kimono")).Content.ReadAsStringAsync());

        Assert.Contains("<summary>Guia de medidas</summary>", html);
        Assert.Contains("<h2>Kimono adulto</h2>", html);
        Assert.Contains("<th scope=\"col\">Altura</th>", html);
        Assert.Matches("<th scope=\"row\">A2</th>\\s*<td>1,70-1,80</td>", html);
        Assert.Contains("Medidas do corpo.", html);
        Assert.Contains("data-shipping-estimate-form", html);
    }

    [Fact]
    public async Task Product_page_leaves_out_the_size_guide_when_the_category_has_none()
    {
        using var factory = Create(new StubCheckout(), Kimono(sizeGuide: false));
        var html = await (await factory.CreateClient().GetAsync("/products/kimono")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("Guia de medidas", html);
    }

    [Fact]
    public async Task Shipping_estimate_returns_options_with_free_shipping_for_the_selected_offer()
    {
        var checkout = new StubCheckout();
        using var factory = Create(checkout);
        using var client = factory.CreateClient();

        var response = await PostEstimateAsync(client, AvailableOffer, "2", "01310-100", json: true);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        var first = root.GetProperty("options")[0];
        Assert.Equal("Grátis", first.GetProperty("price").GetString());
        Assert.True(first.GetProperty("freeShipping").GetBoolean());
        Assert.Equal("R$ 18,50", first.GetProperty("regularPrice").GetString());
        Assert.Equal("R$ 30,00", root.GetProperty("options")[1].GetProperty("price").GetString());
        Assert.Equal(new CartLine(AvailableOffer, 2), Assert.Single(checkout.LastRequest!.Lines));
        Assert.Equal("01310100", checkout.LastRequest.DestinationPostalCode);
    }

    [Theory]
    [InlineData(null, "1", "01310-100", "Escolha a cor e o tamanho")]
    [InlineData("sold-out", "1", "01310-100", "indisponível")]
    [InlineData("available", "11", "01310-100", "quantidade")]
    [InlineData("available", "1", "0131", "CEP válido")]
    public async Task Shipping_estimate_validates_before_calling_the_api(string? offer, string quantity, string postalCode, string message)
    {
        var checkout = new StubCheckout();
        using var factory = Create(checkout);
        using var client = factory.CreateClient();
        var offerId = offer switch { "available" => AvailableOffer, "sold-out" => SoldOutOffer, _ => (Guid?)null };

        var response = await PostEstimateAsync(client, offerId, quantity, postalCode, json: true);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(message, document.RootElement.GetProperty("error").GetString());
        Assert.Null(checkout.LastRequest);
    }

    [Fact]
    public async Task Shipping_estimate_without_javascript_renders_the_options_on_the_page()
    {
        using var factory = Create(new StubCheckout());
        using var client = factory.CreateClient();

        var response = await PostEstimateAsync(client, AvailableOffer, "1", "01310100", json: false);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("value=\"01310-100\"", html);
        Assert.Contains("<em class=\"free-shipping\">Grátis <s>R$ 18,50</s></em>", html);
        Assert.Contains("SEDEX", html);
    }

    [Theory]
    [InlineData(150, "Faltam R$ 50,00 para frete grátis.", false)]
    [InlineData(200, "Você ganhou frete grátis", true)]
    public async Task Cart_shows_progress_towards_the_free_shipping_minimum(int total, string text, bool reached)
    {
        var line = new CatalogQuoteLine { PublicOfferId = AvailableOffer, Quantity = 1, Availability = "available", Presentation = "Kimono", Currency = "BRL", UnitPrice = total, LinePrice = total };
        using var factory = Create(new StubCheckout(freeShippingMinimum: 200m), cart: new CartState(DateTimeOffset.UtcNow, [new(AvailableOffer, 1)]), quote: new CatalogQuoteResult(CatalogLoadState.Success, "BRL", total, [line]));
        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync("/cart")).Content.ReadAsStringAsync());

        Assert.Contains(text, html);
        Assert.Equal(reached, html.Contains("free-shipping-progress is-reached"));
        Assert.Contains("<progress max=\"200\"", html);
    }

    [Fact]
    public async Task Cart_has_no_progress_bar_without_a_free_shipping_minimum()
    {
        var line = new CatalogQuoteLine { PublicOfferId = AvailableOffer, Quantity = 1, Availability = "available", Presentation = "Kimono", Currency = "BRL", UnitPrice = 100, LinePrice = 100 };
        using var factory = Create(new StubCheckout(), cart: new CartState(DateTimeOffset.UtcNow, [new(AvailableOffer, 1)]), quote: new CatalogQuoteResult(CatalogLoadState.Success, "BRL", 100, [line]));
        var html = await (await factory.CreateClient().GetAsync("/cart")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("data-free-shipping-progress", html);
    }

    private static async Task<HttpResponseMessage> PostEstimateAsync(HttpClient client, Guid? offer, string quantity, string postalCode, bool json)
    {
        var page = await (await client.GetAsync("/products/kimono")).Content.ReadAsStringAsync();
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("publicOfferId", offer?.ToString() ?? ""),
            new("quantity", quantity),
            new("postalCode", postalCode)
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/products/kimono?handler=QuoteShipping") { Content = new FormUrlEncodedContent(fields) };
        if (json)
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return await client.SendAsync(request);
    }

    private static Product Kimono(bool sizeGuide = true) => new()
    {
        Slug = "kimono",
        Nome = "Kimono",
        Availability = "available",
        SizeGuide = sizeGuide
            ? ProductSizeGuide.Create("Kimono adulto", ["Tamanho", "Altura"], [new[] { "A1", "1,60-1,70" }, new[] { "A2", "1,70-1,80" }], "Medidas do corpo.")
            : null,
        Variants =
        [
            new ProductVariant
            {
                ColorLabel = "Azul",
                Images = ["/images/azul.jpg"],
                Offers =
                [
                    new ProductOffer { PublicOfferId = AvailableOffer, SizeLabel = "A1", UnitPrice = 100, Currency = "BRL", Availability = "available" },
                    new ProductOffer { PublicOfferId = SoldOutOffer, SizeLabel = "A2", UnitPrice = 100, Currency = "BRL", Availability = "unavailable" }
                ]
            }
        ]
    };

    private static WebApplicationFactory<Program> Create(StubCheckout checkout, Product? product = null, CartState? cart = null, CatalogQuoteResult? quote = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICatalogClient>();
                services.RemoveAll<ICheckoutClient>();
                services.AddScoped<ICatalogClient>(_ => new StubCatalog(product ?? Kimono(), quote ?? CatalogQuoteResult.Unavailable()));
                services.AddScoped<ICheckoutClient>(_ => checkout);
                if (cart is not null)
                {
                    services.RemoveAll<ICartCookieStore>();
                    services.AddScoped<ICartCookieStore>(_ => new FixedCart(cart));
                }
            });
        });

    private sealed class StubCatalog(Product product, CatalogQuoteResult quote) : ICatalogClient
    {
        public Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default) => Task.FromResult(CatalogResult.Empty());
        public Task<ProductDetailResult> GetProductAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(ProductDetailResult.Success(product));
        public Task<CatalogResult> GetRelatedAsync(string slug, int limit = 4, CancellationToken cancellationToken = default) => Task.FromResult(CatalogResult.Empty());
        public Task<CatalogQuoteResult> QuoteAsync(CatalogQuoteRequest request, CancellationToken cancellationToken = default) => Task.FromResult(quote);
    }

    private sealed class StubCheckout(decimal? freeShippingMinimum = null) : ICheckoutClient
    {
        public ShippingQuoteRequest? LastRequest { get; private set; }

        public Task<CheckoutConfigurationResult> GetConfigurationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CheckoutConfigurationResult(CheckoutLoadState.Success, new CheckoutConfiguration { ShippingEnabled = true, FreeShippingMinimumAmount = freeShippingMinimum }));

        public Task<ShippingQuoteResult> QuoteShippingAsync(ShippingQuoteRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ShippingQuoteResult(CheckoutLoadState.Success, new ShippingQuote
            {
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                Currency = "BRL",
                Options =
                [
                    new ShippingQuoteOption { PublicShippingQuoteId = Guid.NewGuid(), ServiceName = "PAC", CarrierName = "Correios", Price = 0, FreeShipping = true, RegularPrice = 18.5m, MinimumDeliveryDays = 4, MaximumDeliveryDays = 7 },
                    new ShippingQuoteOption { PublicShippingQuoteId = Guid.NewGuid(), ServiceName = "SEDEX", CarrierName = "Correios", Price = 30, MinimumDeliveryDays = 1, MaximumDeliveryDays = 2 }
                ]
            }));
        }

        public Task<CheckoutResult> CreateAsync(CheckoutCreateRequest request, string idempotencyKey, string accessToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CheckoutResult> GetAsync(Guid publicCheckoutId, string accessToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CheckoutResult> CancelAsync(Guid publicCheckoutId, string accessToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedCart(CartState state) : ICartCookieStore
    {
        public CartState Read() => state;
        public bool Add(Guid offerId, int quantity, decimal? seenUnitPrice = null) => true;
        public bool Update(Guid offerId, int quantity) => true;
        public bool Remove(Guid offerId) => true;
        public bool Replace(IReadOnlyList<CartLine> lines) => true;
        public void Clear() { }
    }
}
