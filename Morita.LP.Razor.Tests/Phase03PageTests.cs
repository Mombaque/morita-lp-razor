using System;
using System.Net;
using System.Linq;
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

public sealed class Phase03PageTests
{
    [Fact]
    public async Task Listing_preserves_page_filters_and_emits_noindex_without_false_attributes()
    {
        using var factory = Create(new CatalogPage([new Product { Slug = "kimono", Nome = "Kimono" }], 2, 24, 25, 2, CatalogLoadState.Success));
        using var client = factory.CreateClient();
        var html = await (await client.GetAsync("/products?search=kimono&categoryId=7&sizeId=11&available=true&page=2&sort=price-desc")).Content.ReadAsStringAsync();
        Assert.Contains("name=\"search\"", html);
        Assert.Contains("noindex,follow", html);
        Assert.Contains("categoryId=7", html);
        Assert.Contains("sizeId=11", html);
        Assert.DoesNotContain("selected=\"False\"", html);
        Assert.DoesNotContain("disabled=\"False\"", html);
    }

    [Fact]
    public async Task Kids_route_uses_first_class_audience_filter_and_commerce_navigation()
    {
        using var factory = Create(new CatalogPage([
            new Product { Slug = "kimono-kids", Nome = "Kimono Kids", Audience = PublicCatalogAudience.Kids }
        ], 1, 24, 1, 1, CatalogLoadState.Success));
        using var client = factory.CreateClient();

        var html = await (await client.GetAsync("/kids?modality=jiu-jitsu")).Content.ReadAsStringAsync();

        Assert.Contains("Kimono Kids", html);
        Assert.Contains("href=\"/kids\" class=\"nav-link active\"", html);
        Assert.Equal(PublicCatalogAudience.Kids, Stub.LastCatalogQuery!.Audience);
        Assert.Equal("jiu-jitsu", Stub.LastCatalogQuery.Modality);
        Assert.True(Stub.LastCatalogQuery.Available);
    }

    [Fact]
    public async Task Home_uses_catalog_products_and_renders_honest_empty_state()
    {
        using var productFactory = Create(new CatalogPage([
            new Product { Slug = "luva", Nome = "Luva catálogo", Imagens = ["https://cdn.example/luva.jpg"] }
        ], 1, 24, 1, 1, CatalogLoadState.Success));
        var productHtml = WebUtility.HtmlDecode(await (await productFactory.CreateClient().GetAsync("/")).Content.ReadAsStringAsync());
        Assert.Contains("Luva catálogo", productHtml);
        Assert.Contains("https://cdn.example/luva.jpg", productHtml);

        using var emptyFactory = Create(new CatalogPage([], 1, 24, 0, 0, CatalogLoadState.Empty));
        var emptyHtml = await (await emptyFactory.CreateClient().GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains("Novos produtos entrando no corner", emptyHtml);
    }

    [Fact]
    public async Task Detail_renders_opaque_offer_matrix_and_disabled_unavailable_offer()
    {
        var available = Guid.NewGuid();
        var unavailable = Guid.NewGuid();
        var product = new Product { Slug = "kimono", Nome = "Kimono", Variants = [new ProductVariant { ColorLabel = "Azul", Offers = [new ProductOffer { PublicOfferId = available, SizeLabel = "A1", Availability = "available" }, new ProductOffer { PublicOfferId = unavailable, SizeLabel = "A2", Availability = "unavailable" }] }] };
        using var factory = CreateDetail(product);
        using var client = factory.CreateClient();
        var html = WebUtility.HtmlDecode(await (await client.GetAsync($"/products/kimono?publicOfferId={available}&quantity=2")).Content.ReadAsStringAsync());
        Assert.Contains($"name=\"publicOfferId\" value=\"{available}\"", html);
        Assert.Contains("Azul / A1", html);
        Assert.Contains("disabled=\"disabled\"", html);
        Assert.DoesNotContain("name=\"color\"", html);
        Assert.Contains("application/ld+json", html);
    }

    [Fact]
    public async Task Unavailable_product_is_rendered_and_not_mistaken_for_not_found()
    {
        using var factory = CreateDetail(new Product { Slug = "paused", Nome = "Paused", Availability = "unavailable" });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/products/paused");
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Produto temporariamente indisponível", body);
    }

    [Fact]
    public async Task Missing_product_is_a_real_404()
    {
        using var factory = CreateDetail(null, ProductDetailResult.NotFound());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/products/missing")).StatusCode);
    }

    [Fact]
    public async Task Upstream_detail_failure_is_noindex_canonicalized_and_has_no_product_metadata()
    {
        using var factory = CreateDetail(null, ProductDetailResult.Unavailable());
        var html = await (await factory.CreateClient().GetAsync("/products/missing-upstream")).Content.ReadAsStringAsync();
        Assert.Contains("name=\"robots\" content=\"noindex,follow\"", html);
        Assert.Contains("https://moritafight.com.br/products/missing-upstream", html);
        Assert.DoesNotContain("application/ld+json", html);
        Assert.DoesNotContain("property=\"og:image\"", html);
    }

    [Fact]
    public async Task Quote_success_is_required_before_continuation_cta()
    {
        var offer = Guid.NewGuid();
        var product = new Product { Slug = "quoted", Nome = "Quoted", Variants = [new ProductVariant { ColorLabel = "Preto", Offers = [new ProductOffer { PublicOfferId = offer, SizeLabel = "M", Availability = "available" }] }] };
        using var successFactory = CreateDetail(product, quote: new CatalogQuoteResult(CatalogLoadState.Success, "BRL", 10, []));
        var successHtml = await (await successFactory.CreateClient().GetAsync($"/products/quoted?publicOfferId={offer}&quantity=1")).Content.ReadAsStringAsync();
        Assert.Contains("method=\"get\"", successHtml);
        Assert.Contains("action=\"/products/quoted\"", successHtml);
        Assert.Contains("name=\"publicOfferId\"", successHtml);
        Assert.Contains("name=\"quantity\"", successHtml);
        Assert.DoesNotContain("name=\"color\"", successHtml);
        Assert.DoesNotContain("name=\"offer\"", successHtml);
        Assert.Contains("Continuar com atendimento", successHtml);
        Assert.Contains("wa.me/5515981079332?text=", successHtml);
        Assert.Contains("Preto", successHtml);
        Assert.Contains("quantidade%201", successHtml);
        Assert.Equal(offer, Stub.LastQuoteRequest!.Lines.Single().PublicOfferId);
        Assert.Equal(1, Stub.LastQuoteRequest.Lines.Single().Quantity);
        using var failedFactory = CreateDetail(product, quote: CatalogQuoteResult.Unavailable());
        var failedHtml = await (await failedFactory.CreateClient().GetAsync($"/products/quoted?publicOfferId={offer}&quantity=1")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Continuar com atendimento", failedHtml);
    }

    [Fact]
    public async Task Invalid_or_unavailable_selection_never_confirms()
    {
        var unavailable = Guid.NewGuid();
        var product = new Product { Slug = "check", Nome = "Check", Variants = [new ProductVariant { Offers = [new ProductOffer { PublicOfferId = unavailable, Availability = "unavailable" }] }] };
        using var factory = CreateDetail(product, quote: new CatalogQuoteResult(CatalogLoadState.Success, "BRL", 10, []));
        var html = await (await factory.CreateClient().GetAsync($"/products/check?publicOfferId={Guid.NewGuid()}&quantity=0")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Continuar com atendimento", html);
        Assert.Contains("quantidade", html, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> Create(CatalogPage page) => CreateDetail(new Product { Slug = "x", Nome = "x" }, page: page);
    private static WebApplicationFactory<Program> CreateDetail(Product? detail, ProductDetailResult? forced = null, CatalogPage? page = null, CatalogQuoteResult? quote = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICatalogClient>();
                services.AddScoped<ICatalogClient>(_ => new Stub(
                    page ?? new CatalogPage([], 1, 24, 0, 0, CatalogLoadState.Empty),
                    CatalogResult.Empty(),
                    forced ?? (detail is null ? ProductDetailResult.NotFound() : ProductDetailResult.Success(detail)), quote ?? CatalogQuoteResult.Unavailable()));
            });
        });
    }

    private sealed class Stub(CatalogPage page, CatalogResult related, ProductDetailResult detail, CatalogQuoteResult quote) : ICatalogClient
    {
        public static CatalogQuoteRequest? LastQuoteRequest { get; private set; }
        public static CatalogQuery? LastCatalogQuery { get; private set; }
        public Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default) => Task.FromResult(CatalogResult.Empty());
        public Task<CatalogPage> GetCatalogAsync(CatalogQuery query, CancellationToken cancellationToken = default) { LastCatalogQuery = query; return Task.FromResult(page); }
        public Task<CatalogFilters?> GetFiltersAsync(CancellationToken cancellationToken = default) => Task.FromResult<CatalogFilters?>(new());
        public Task<ProductDetailResult> GetProductAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(detail);
        public Task<CatalogResult> GetRelatedAsync(string slug, int limit = 4, CancellationToken cancellationToken = default) => Task.FromResult(related);
        public Task<CatalogQuoteResult> QuoteAsync(CatalogQuoteRequest request, CancellationToken cancellationToken = default) { LastQuoteRequest = request; return Task.FromResult(quote); }
    }
}
