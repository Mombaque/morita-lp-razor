using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class CartPageTests
{
    [Fact]
    public async Task Empty_cart_renders_accessible_state_and_header_count()
    {
        using var factory = CreateFactory(new CartState(DateTimeOffset.UtcNow, []), CatalogQuoteResult.Unavailable());
        var response = await factory.CreateClient().GetAsync("/cart");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-cart=\"empty\"", body);
        Assert.Contains("data-cart=\"count\">0", body);
        Assert.Contains("noindex,nofollow", body);
        Assert.Contains("Ver produtos", body);
    }

    [Fact]
    public async Task Mixed_statuses_render_variants_prices_and_continue_action()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var state = new CartState(DateTimeOffset.UtcNow, [new(first, 2), new(second, 3)]);
        var quote = new CatalogQuoteResult(CatalogLoadState.Partial, "BRL", 20, [
            new() { PublicOfferId = first, Quantity = 2, Availability = "available", Presentation = "Kimono", ColorLabel = "Azul", SizeLabel = "A1", Currency = "BRL", UnitPrice = 10, LinePrice = 20 },
            new() { PublicOfferId = second, Quantity = 3, Availability = "insufficient", Presentation = "Faixa", ColorLabel = "Preta", SizeLabel = "M", Currency = "BRL", UnitPrice = 12, LinePrice = 36 }
        ]);
        using var factory = CreateFactory(state, quote);
        var response = await factory.CreateClient().GetAsync("/cart");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-cart=\"page\"", body);
        Assert.Contains("Azul / A1", body);
        Assert.Contains("Preta / M", body);
        Assert.Contains("Quantidade indispon", body);
        Assert.Contains("R$ 12.00", body);
        Assert.Contains("data-cart=\"continue-shopping\"", body);
        Assert.Contains("data-cart=\"quantity-stepper\"", body);
        Assert.Contains("data-cart=\"quantity-decrement\"", body);
        Assert.Contains("data-cart=\"quantity-increment\"", body);
        Assert.Contains("data-cart=\"quantity-input\"", body);
        Assert.Contains("cart-button-primary", body);
        Assert.Contains("cart-button-danger", body);
        Assert.Contains("data-cart=\"checkout\"", body);
    }

    [Fact]
    public async Task Insufficient_only_cart_keeps_quantity_controls_and_disables_checkout()
    {
        var offer = Guid.NewGuid();
        var state = new CartState(DateTimeOffset.UtcNow, [new(offer, 4)]);
        var quote = new CatalogQuoteResult(CatalogLoadState.Partial, "BRL", 0, [
            new() { PublicOfferId = offer, Quantity = 4, Availability = "insufficient", Presentation = "Kimono", Currency = "BRL", UnitPrice = 10, LinePrice = 40 }
        ]);
        using var factory = CreateFactory(state, quote);

        var body = await (await factory.CreateClient().GetAsync("/cart")).Content.ReadAsStringAsync();

        Assert.Contains("data-cart=\"quantity-stepper\"", body);
        Assert.Contains("data-cart=\"quantity-input\"", body);
        Assert.Contains("disabled data-cart=\"checkout\"", body);
        Assert.Contains("data-cart=\"correction\"", body);
        Assert.DoesNotContain("data-cart=\"total\"", body);
    }

    [Fact]
    public async Task Quote_unavailable_preserves_opaque_state_and_mutations_report_failures()
    {
        var state = new CartState(DateTimeOffset.UtcNow, [new(Guid.NewGuid(), 2)]);
        var cart = new TestCart(state) { UpdateResult = false };
        using var factory = CreateFactory(cart, CatalogQuoteResult.Unavailable());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var body = await (await client.GetAsync("/cart")).Content.ReadAsStringAsync();
        Assert.Contains("Seus itens foram preservados", body);
        var token = Regex.Match(body, "name=\\\"request-verification-token\\\" content=\\\"([^\\\"]+)").Groups[1].Value;
        var mutation = await client.PostAsync("/cart?handler=Update", new FormUrlEncodedContent([
            new KeyValuePair<string, string>("publicOfferId", state.Lines[0].PublicOfferId.ToString()),
            new KeyValuePair<string, string>("quantity", "99"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));
        Assert.Equal(HttpStatusCode.Redirect, mutation.StatusCode);
        var followup = await client.GetAsync("/cart");
        var followupBody = await followup.Content.ReadAsStringAsync();
        Assert.Contains("atualizar este item", followupBody);
        Assert.Equal(state.Lines, cart.Read().Lines);
    }

    [Fact]
    public async Task Failed_availability_preflight_preserves_cart_without_writing_cookie()
    {
        var offer = Guid.NewGuid();
        var state = new CartState(DateTimeOffset.UtcNow, [new(offer, 2)]);
        var cart = new TestCart(state);
        using var factory = CreateFactory(cart, CatalogQuoteResult.Unavailable());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var body = await (await client.GetAsync("/cart")).Content.ReadAsStringAsync();
        var token = Regex.Match(body, "name=\\\"request-verification-token\\\" content=\\\"([^\\\"]+)").Groups[1].Value;

        var mutation = await client.PostAsync("/cart?handler=Update", new FormUrlEncodedContent([
            new KeyValuePair<string, string>("publicOfferId", offer.ToString()),
            new KeyValuePair<string, string>("quantity", "1"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));

        Assert.Equal(HttpStatusCode.Redirect, mutation.StatusCode);
        Assert.Equal(0, cart.UpdateCalls);
        Assert.Equal(state.Lines, cart.Read().Lines);
        var followup = await client.GetAsync("/cart");
        Assert.Contains("Seus itens foram preservados", await followup.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Insufficient_preflight_preserves_cart_without_writing_cookie()
    {
        var offer = Guid.NewGuid();
        var state = new CartState(DateTimeOffset.UtcNow, [new(offer, 2)]);
        var cart = new TestCart(state);
        var initialQuote = CatalogQuoteResult.Success("BRL", 20, [
            new() { PublicOfferId = offer, Quantity = 2, Availability = "available", Presentation = "Kimono", Currency = "BRL", UnitPrice = 10, LinePrice = 20 }
        ]);
        var mutationQuote = new CatalogQuoteResult(CatalogLoadState.Partial, "BRL", 0, [
            new() { PublicOfferId = offer, Quantity = 1, Availability = "insufficient", Presentation = "Kimono", Currency = "BRL", UnitPrice = 10, LinePrice = 10 }
        ]);
        using var factory = CreateFactory(cart, [initialQuote, mutationQuote]);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var body = await (await client.GetAsync("/cart")).Content.ReadAsStringAsync();
        var token = Regex.Match(body, "name=\\\"request-verification-token\\\" content=\\\"([^\\\"]+)").Groups[1].Value;

        var mutation = await client.PostAsync("/cart?handler=Update", new FormUrlEncodedContent([
            new KeyValuePair<string, string>("publicOfferId", offer.ToString()),
            new KeyValuePair<string, string>("quantity", "1"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));

        Assert.Equal(HttpStatusCode.Redirect, mutation.StatusCode);
        Assert.Equal(0, cart.UpdateCalls);
        Assert.Equal(state.Lines, cart.Read().Lines);
        var responseBody = await (await client.GetAsync("/cart")).Content.ReadAsStringAsync();
        Assert.Contains("A quantidade solicitada n", responseBody);
        Assert.Contains("Reduza a quantidade", responseBody);
    }

    [Fact]
    public async Task Available_lower_quantity_is_written_after_preflight()
    {
        var offer = Guid.NewGuid();
        var state = new CartState(DateTimeOffset.UtcNow, [new(offer, 4)]);
        var cart = new TestCart(state);
        var initialQuote = new CatalogQuoteResult(CatalogLoadState.Partial, "BRL", 0, [
            new() { PublicOfferId = offer, Quantity = 4, Availability = "insufficient", Presentation = "Kimono", Currency = "BRL", UnitPrice = 10 }
        ]);
        var mutationQuote = CatalogQuoteResult.Success("BRL", 10, [
            new() { PublicOfferId = offer, Quantity = 1, Availability = "available", Presentation = "Kimono", Currency = "BRL", UnitPrice = 10, LinePrice = 10 }
        ]);
        using var factory = CreateFactory(cart, [initialQuote, mutationQuote]);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var body = await (await client.GetAsync("/cart")).Content.ReadAsStringAsync();
        var token = Regex.Match(body, "name=\\\"request-verification-token\\\" content=\\\"([^\\\"]+)").Groups[1].Value;

        var mutation = await client.PostAsync("/cart?handler=Update", new FormUrlEncodedContent([
            new KeyValuePair<string, string>("publicOfferId", offer.ToString()),
            new KeyValuePair<string, string>("quantity", "1"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));

        Assert.Equal(HttpStatusCode.Redirect, mutation.StatusCode);
        Assert.Equal(1, cart.UpdateCalls);
        Assert.Equal(1, cart.LastUpdatedQuantity);
    }

    [Fact]
    public async Task Product_add_validates_quantity_redirects_and_requires_antiforgery()
    {
        var offer = Guid.NewGuid();
        var product = new Product { Slug = "kimono", Nome = "Kimono", Variants = [new ProductVariant { ColorLabel = "Azul", Offers = [new ProductOffer { PublicOfferId = offer, Availability = "available" }] }] };
        var cart = new TestCart(new CartState(DateTimeOffset.UtcNow, []));
        using var factory = CreateFactory(cart, CatalogQuoteResult.Success("BRL", 10, [new CatalogQuoteLine { PublicOfferId = offer, Quantity = 1, Availability = "available", UnitPrice = 10, LinePrice = 10, Currency = "BRL" }]), product);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/products/kimono?handler=Add", new FormUrlEncodedContent([
            new KeyValuePair<string, string>("publicOfferId", offer.ToString()), new KeyValuePair<string, string>("quantity", "1")
        ]))).StatusCode);
        var invalid = await client.GetAsync($"/products/kimono?publicOfferId={offer}&quantity=11");
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("A quantidade deve estar entre 1 e 10.", await invalid.Content.ReadAsStringAsync());
        var productPage = await client.GetAsync($"/products/kimono?publicOfferId={offer}&quantity=1");
        var productBody = await productPage.Content.ReadAsStringAsync();
        var token = Regex.Match(productBody, "name=\\\"request-verification-token\\\" content=\\\"([^\\\"]+)").Groups[1].Value;
        Assert.NotEmpty(token);
        var added = await client.PostAsync($"/products/kimono?handler=Add", new FormUrlEncodedContent([
            new KeyValuePair<string, string>("publicOfferId", offer.ToString()), new KeyValuePair<string, string>("quantity", "1"), new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(CartState state, CatalogQuoteResult quote, Product? product = null) => CreateFactory(new TestCart(state), quote, product);
    private static WebApplicationFactory<Program> CreateFactory(TestCart cart, CatalogQuoteResult quote, Product? product = null) =>
        CreateFactory(cart, [quote], product);
    private static WebApplicationFactory<Program> CreateFactory(TestCart cart, IReadOnlyList<CatalogQuoteResult> quotes, Product? product = null)
    {
        var quoteQueue = new Queue<CatalogQuoteResult>(quotes);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICartCookieStore>();
                services.RemoveAll<ICatalogClient>();
                services.AddScoped<ICartCookieStore>(_ => cart);
                services.AddScoped<ICatalogClient>(_ => new StubClient(quoteQueue, product));
            });
        });
    }

    private sealed class TestCart(CartState state) : ICartCookieStore
    {
        public bool UpdateResult { get; set; } = true;
        public int UpdateCalls { get; private set; }
        public int? LastUpdatedQuantity { get; private set; }
        public CartState Read() => state;
        public bool Add(Guid offerId, int quantity) => true;
        public bool Update(Guid offerId, int quantity) { UpdateCalls++; LastUpdatedQuantity = quantity; return UpdateResult; }
        public bool Remove(Guid offerId) => true;
        public bool Replace(IReadOnlyList<CartLine> lines) => true;
        public void Clear() { }
    }

    private sealed class StubClient(Queue<CatalogQuoteResult> quotes, Product? product) : ICatalogClient
    {
        public Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default) => Task.FromResult(CatalogResult.Empty());
        public Task<ProductDetailResult> GetProductAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(product is null ? ProductDetailResult.Unavailable() : ProductDetailResult.Success(product));
        public Task<CatalogQuoteResult> QuoteAsync(CatalogQuoteRequest request, CancellationToken cancellationToken = default) => Task.FromResult(quotes.Count > 1 ? quotes.Dequeue() : quotes.Peek());
    }
}
