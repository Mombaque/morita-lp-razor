using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class StorefrontTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public StorefrontTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICatalogClient>();
                services.AddScoped<ICatalogClient>(_ => new StubCatalogClient(CatalogResult.Empty(), CatalogResult.Empty()));
            });
            builder.UseSetting("Storefront:ProductSource", "Api");
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task Commerce_pages_render_lowercase_canonical_links_and_navigation()
    {
        var response = await _client.GetAsync("/jiu-jitsu");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Jiu-", body);
        Assert.Contains("Nenhum produto encontrado", body);
        Assert.Contains("href=\"/muay-thai\"", body);
        Assert.Contains("https://moritafight.com.br/jiu-jitsu", body);
        Assert.Contains("GTM-TK3DKRF9", body);
        Assert.Contains("<a href=\"/jiu-jitsu\"", body);
        Assert.Contains("class=\"nav-link active\"", body);
        Assert.Contains("aria-current=\"page\"", body);
        Assert.DoesNotContain("href=\"/muay-thai\" class=\"nav-link active\"", body);
        Assert.Contains("Entrega para todo o Brasil", body);
    }

    [Theory]
    [InlineData("/JiuJitsu", "/jiu-jitsu?campaign=test")]
    [InlineData("/MuayThai", "/muay-thai?utm_source=test")]
    public async Task Legacy_case_routes_permanently_redirect_with_query(string path, string expected)
    {
        var response = await _client.GetAsync(path + expected[expected.IndexOf('?')..]);
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Health_is_available_without_catalog_dependency()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", (await response.Content.ReadFromJsonAsync<HealthResponse>())!.status);
    }

    [Fact]
    public async Task Active_navigation_follows_the_lowercase_request_path()
    {
        var home = await (await _client.GetAsync("/")).Content.ReadAsStringAsync();
        var muay = await (await _client.GetAsync("/muay-thai")).Content.ReadAsStringAsync();
        Assert.Contains("<a href=\"/\" class=\"nav-link active\"", home);
        Assert.Contains("<a href=\"/muay-thai\" class=\"nav-link active\"", muay);
    }

    [Fact]
    public async Task Api_mode_renders_absolute_image_price_and_preserves_metadata()
    {
        using var factory = CreateFactory(CatalogResult.Success([new Product
        {
            Nome = "API Kimono", Descricao = "Descrição API", FormattedPrice = "R$ 99,90",
            Imagens = ["https://objects.example/catalog/kimono.jpg"]
        }]), CatalogResult.Empty());
        using var client = factory.CreateClient();
        var body = await (await client.GetAsync("/jiu-jitsu")).Content.ReadAsStringAsync();

        Assert.Contains("https://objects.example/catalog/kimono.jpg", body);
        Assert.Contains("R$ 99,90", body);
        Assert.Contains("og:url", body);
        Assert.Contains("data-track-event=\"customer_product_request_open\"", body);
        Assert.Contains("https://moritafight.com.br/jiu-jitsu", body);
    }

    [Fact]
    public async Task Api_mode_renders_home_empty_state_without_carousel_controls()
    {
        using var factory = CreateFactory(CatalogResult.Empty(), CatalogResult.Empty());
        using var client = factory.CreateClient();
        var body = await (await client.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains("Novos produtos entrando no corner", body);
        Assert.DoesNotContain("random-carousel-btn", body);
    }

    [Fact]
    public async Task Api_mode_renders_category_unavailable_state_without_legacy_products()
    {
        using var factory = CreateFactory(CatalogResult.Unavailable(), CatalogResult.Empty());
        using var client = factory.CreateClient();
        var body = await (await client.GetAsync("/jiu-jitsu")).Content.ReadAsStringAsync();
        Assert.Contains("Catálogo temporariamente indisponível", body);
        Assert.DoesNotContain("Faixas de Jiu-Jitsu", body);
    }

    [Fact]
    public async Task Api_mode_keeps_healthy_home_products_during_a_partial_outage()
    {
        using var factory = CreateFactory(CatalogResult.Success([new Product
        {
            Nome = "API Kimono", Descricao = "Descrição API", Imagens = ["https://objects.example/catalog/kimono.jpg"]
        }]), CatalogResult.Unavailable());
        using var client = factory.CreateClient();

        var body = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Contains("Parte do catálogo está temporariamente indisponível", body);
        Assert.Contains("https://objects.example/catalog/kimono.jpg", body);
        Assert.Contains("API Kimono", body);
    }

    private static WebApplicationFactory<Program> CreateFactory(CatalogResult jiuJitsu, CatalogResult muayThai) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICatalogClient>();
                services.AddScoped<ICatalogClient>(_ => new StubCatalogClient(jiuJitsu, muayThai));
            });
            builder.UseSetting("Storefront:ProductSource", "Api");
        });

    private sealed class StubCatalogClient(CatalogResult jiuJitsu, CatalogResult muayThai) : ICatalogClient
    {
        public Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default) =>
            Task.FromResult(modality == "jiu-jitsu" ? jiuJitsu : muayThai);
        public Task<CatalogPage> GetCatalogAsync(CatalogQuery query, CancellationToken cancellationToken = default)
        {
            var source = query.Modality == "jiu-jitsu" ? jiuJitsu : query.Modality == "muay-thai" ? muayThai : Combine();
            var products = query.Audience == PublicCatalogAudience.Kids ? source.Products.Where(product => product.Audience == PublicCatalogAudience.Kids).ToList() : source.Products;
            var state = products.Count > 0 ? source.State : source.State == CatalogLoadState.Unavailable ? CatalogLoadState.Unavailable : CatalogLoadState.Empty;
            return Task.FromResult(new CatalogPage(products, 1, CatalogQuery.PageSize, products.Count, products.Count > 0 ? 1 : 0, state));
        }
        public Task<CatalogFilters?> GetFiltersAsync(CancellationToken cancellationToken = default) => Task.FromResult<CatalogFilters?>(new());
        private CatalogResult Combine()
        {
            var products = jiuJitsu.Products.Concat(muayThai.Products).ToList();
            if (products.Count > 0 && (jiuJitsu.State == CatalogLoadState.Unavailable || muayThai.State == CatalogLoadState.Unavailable)) return new(CatalogLoadState.Partial, products);
            if (products.Count > 0) return CatalogResult.Success(products);
            return jiuJitsu.State == CatalogLoadState.Unavailable || muayThai.State == CatalogLoadState.Unavailable ? CatalogResult.Unavailable() : CatalogResult.Empty();
        }
    }

    private sealed record HealthResponse(string status);
}
