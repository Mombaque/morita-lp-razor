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
        Assert.Contains("data-filter-open", html);
        Assert.Contains("data-filter-drawer", html);
        Assert.Contains("catalog-filter-count\">4", html);
        Assert.Contains("name=\"modalityId\"", html);
        Assert.Contains("name=\"brandId\"", html);
        Assert.Contains("name=\"colorId\"", html);
        Assert.Contains("name=\"available\"", html);
        Assert.Contains("name=\"audience\"", html);
        Assert.Contains("name=\"minimumPrice\"", html);
        Assert.Contains("name=\"maximumPrice\"", html);
        Assert.Contains("data-catalog-sort", html);
        Assert.DoesNotContain("class=\"catalog-filters\"", html);
        Assert.True(html.IndexOf("catalog-toolbar", StringComparison.Ordinal) < html.IndexOf("storefront-products", StringComparison.Ordinal));
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
    public async Task Kids_route_preserves_category_filter_with_first_class_audience()
    {
        using var factory = Create(new CatalogPage([], 1, 24, 0, 0, CatalogLoadState.Empty));
        using var client = factory.CreateClient();

        await client.GetAsync("/kids?category=kimonos");

        Assert.Equal("kimonos", Stub.LastCatalogQuery!.Category);
        Assert.Equal(PublicCatalogAudience.Kids, Stub.LastCatalogQuery.Audience);
        Assert.True(Stub.LastCatalogQuery.Available);
    }

    [Fact]
    public async Task Category_route_combines_category_brand_and_sort_filters()
    {
        using var factory = Create(
            new CatalogPage([new Product { Slug = "kimono", Nome = "Kimono" }], 1, 24, 1, 1, CatalogLoadState.Success),
            new CatalogFilters
            {
                Categories = [new CatalogFilter { Id = 7, Slug = "kimonos", Label = "Kimonos" }],
                Suppliers = [new CatalogFilter { Id = 4, Slug = "itg", Label = "In The Guard" }]
            });
        using var client = factory.CreateClient();

        var html = await (await client.GetAsync("/jiu-jitsu?category=kimonos&brandId=4&sort=price-desc")).Content.ReadAsStringAsync();

        Assert.Equal("kimonos", Stub.LastCatalogQuery!.Category);
        Assert.Equal(4, Stub.LastCatalogQuery.BrandId);
        Assert.Equal("price-desc", Stub.LastCatalogQuery.Sort);
        Assert.Equal("jiu-jitsu", Stub.LastCatalogQuery.Modality);
        Assert.Contains("Kimonos / In The Guard", html);
        Assert.Contains("href=\"/jiu-jitsu?category=kimonos&amp;brandId=4&amp;sort=price-desc\"", html);
        Assert.Contains("href=\"/jiu-jitsu?category=kimonos&amp;sort=price-desc\"", html);
        Assert.Contains("href=\"/jiu-jitsu?brandId=4&amp;sort=price-desc\"", html);
        Assert.Contains("Limpar filtros", html);
    }

    [Fact]
    public async Task Kids_route_preserves_modality_when_combining_brand_and_sort()
    {
        using var factory = Create(
            new CatalogPage([], 1, 24, 0, 0, CatalogLoadState.Empty),
            new CatalogFilters
            {
                Suppliers = [new CatalogFilter { Id = 4, Slug = "itg", Label = "In The Guard" }]
            });
        using var client = factory.CreateClient();

        await client.GetAsync("/kids?modality=jiu-jitsu&brandId=4&sort=name-asc");

        Assert.Equal("jiu-jitsu", Stub.LastCatalogQuery!.Modality);
        Assert.Equal(4, Stub.LastCatalogQuery.BrandId);
        Assert.Equal("name-asc", Stub.LastCatalogQuery.Sort);
    }

    [Fact]
    public async Task Product_cards_render_bounded_available_offer_rows_with_exact_ids()
    {
        var product = new Product
        {
            Slug = "matrix",
            Nome = "Matrix",
            FormattedPrice = "R$ 99,00",
            Variants = Enumerable.Range(0, 4).Select(index => new ProductVariant
            {
                ColorId = index + 1,
                ColorLabel = $"Cor {index + 1}",
                ColorHex = "#1255CC",
                Offers = Enumerable.Range(0, 8).Select(size => new ProductOffer
                {
                    PublicOfferId = Guid.NewGuid(),
                    SizeLabel = $"A{size}",
                    UnitPrice = 100 + size,
                    Availability = size == 7 ? "unavailable" : "available"
                }).ToList()
            }).ToList()
        };
        using var factory = Create(new CatalogPage([product], 1, 24, 1, 1, CatalogLoadState.Success));

        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync("/products")).Content.ReadAsStringAsync());
        Assert.Equal(3, html.Split("class=\"product-variant-row\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("+1 cores", html);
        Assert.Equal(3, html.Split("+1 tamanhos", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain(">A7<", html);
        Assert.Contains("data-product-offer", html);
        Assert.Contains("data-offer-id=\"", html);
        Assert.Contains("#1255CC", html);
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
    public async Task Product_card_carousel_script_is_shared_across_all_card_surfaces()
    {
        var cardProduct = new Product
        {
            Slug = "kimono",
            Nome = "Kimono",
            Imagens = ["https://cdn.example/kimono-front.jpg", "https://cdn.example/kimono-back.jpg"]
        };
        var related = CatalogResult.Success([
            new Product
            {
                Slug = "related",
                Nome = "Related",
                Imagens = ["https://cdn.example/related-front.jpg", "https://cdn.example/related-back.jpg"]
            }
        ]);
        using var factory = CreateDetail(
            new Product { Slug = "detail", Nome = "Detail" },
            page: new CatalogPage([cardProduct], 1, 24, 1, 1, CatalogLoadState.Success),
            related: related);
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/", "/products", "/jiu-jitsu", "/muay-thai", "/kids" })
        {
            var html = await (await client.GetAsync(path)).Content.ReadAsStringAsync();
            AssertCarouselScriptLoadedOnce(html);
            AssertProductCardScriptLoadedOnce(html);
        }

        var detailHtml = await (await client.GetAsync("/products/detail")).Content.ReadAsStringAsync();
        AssertCarouselScriptLoadedOnce(detailHtml);
        AssertProductCardScriptLoadedOnce(detailHtml);
        Assert.Contains("class=\"prev\" type=\"button\"", detailHtml);
        Assert.Contains("class=\"next\" type=\"button\"", detailHtml);
    }

    [Fact]
    public async Task Detail_renders_opaque_offer_matrix_and_disabled_unavailable_offer()
    {
        var available = Guid.NewGuid();
        var unavailable = Guid.NewGuid();
        var product = new Product { Slug = "kimono", Nome = "Kimono", Variants = [new ProductVariant { ColorLabel = "Azul", Images = ["/images/azul.jpg"], Offers = [new ProductOffer { PublicOfferId = available, SizeLabel = "A1", UnitPrice = 1234.5m, Availability = "available" }, new ProductOffer { PublicOfferId = unavailable, SizeLabel = "A2", Availability = "unavailable" }] }] };
        using var factory = CreateDetail(product);
        using var client = factory.CreateClient();
        var html = WebUtility.HtmlDecode(await (await client.GetAsync($"/products/kimono?publicOfferId={available}&quantity=2")).Content.ReadAsStringAsync());
        Assert.Contains($"name=\"publicOfferId\" value=\"{available}\"", html);
        Assert.Contains("Azul / A1", html);
        Assert.Contains("disabled=\"disabled\"", html);
        Assert.DoesNotContain("name=\"color\"", html);
        Assert.Contains("application/ld+json", html);
        Assert.Contains("novalidate", html);
        Assert.Contains("offer-validation-message", html);
        Assert.Contains("data-price=\"1234.5\"", html);
        Assert.Contains("id=\"detail-image\" src=\"/images/azul.jpg\"", html);
    }

    [Fact]
    public async Task Product_card_offer_anchor_has_exact_detail_fallback()
    {
        var offer = Guid.NewGuid();
        var product = new Product { Slug = "kimono", Nome = "Kimono", Variants = [new ProductVariant { ColorLabel = "Preto", Offers = [new ProductOffer { PublicOfferId = offer, SizeLabel = "M", Availability = "available" }] }] };
        using var factory = Create(new CatalogPage([product], 1, 24, 1, 1, CatalogLoadState.Success));

        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync("/products")).Content.ReadAsStringAsync());

        Assert.Contains($"href=\"/products/kimono?publicOfferId={offer}\"", html);
        Assert.Contains("class=\"product-variant-size\"", html);
    }

    [Fact]
    public async Task Detail_renders_gallery_navigation_for_multiple_images()
    {
        var product = new Product
        {
            Slug = "gallery",
            Nome = "Gallery product",
            Imagens = ["/images/one.jpg", "/images/two.jpg"]
        };
        using var factory = CreateDetail(product);

        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync("/products/gallery")).Content.ReadAsStringAsync());

        Assert.Contains("data-gallery", html);
        Assert.Contains("data-gallery-prev", html);
        Assert.Contains("aria-label=\"Imagem anterior\"", html);
        Assert.Contains("data-gallery-next", html);
        Assert.Contains("aria-label=\"Próxima imagem\"", html);
        Assert.Contains("aria-pressed=\"true\"", html);
        Assert.Contains("aria-pressed=\"false\"", html);
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
    public async Task Detail_posts_selected_offer_directly_to_cart()
    {
        var offer = Guid.NewGuid();
        var product = new Product { Slug = "quoted", Nome = "Quoted", Variants = [new ProductVariant { ColorLabel = "Preto", Offers = [new ProductOffer { PublicOfferId = offer, SizeLabel = "M", Availability = "available" }] }] };
        using var factory = CreateDetail(product);
        var html = await (await factory.CreateClient().GetAsync("/products/quoted")).Content.ReadAsStringAsync();

        Assert.Contains("method=\"post\"", html);
        Assert.Contains("data-cart=\"add\"", html);
        Assert.Contains("Adicionar ao carrinho", html);
        Assert.Contains("name=\"publicOfferId\"", html);
        Assert.Contains("name=\"quantity\"", html);
        Assert.DoesNotContain("Confirmar seleção", html);
        Assert.DoesNotContain("Seleção confirmada", html);
        Assert.DoesNotContain("Continuar com atendimento", html);
        Assert.DoesNotContain("wa.me/5515981079332?text=", html);
        Assert.DoesNotContain("name=\"color\"", html);
        Assert.DoesNotContain("name=\"offer\"", html);
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

    private static WebApplicationFactory<Program> Create(CatalogPage page, CatalogFilters? filters = null) => CreateDetail(new Product { Slug = "x", Nome = "x" }, page: page, filters: filters);
    private static WebApplicationFactory<Program> CreateDetail(Product? detail, ProductDetailResult? forced = null, CatalogPage? page = null, CatalogQuoteResult? quote = null, CatalogResult? related = null, CatalogFilters? filters = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICatalogClient>();
                services.AddScoped<ICatalogClient>(_ => new Stub(
                    page ?? new CatalogPage([], 1, 24, 0, 0, CatalogLoadState.Empty),
                    related ?? CatalogResult.Empty(),
                    forced ?? (detail is null ? ProductDetailResult.NotFound() : ProductDetailResult.Success(detail)), quote ?? CatalogQuoteResult.Unavailable(), filters ?? new()));
            });
        });
    }

    private static void AssertCarouselScriptLoadedOnce(string html)
    {
        Assert.Equal(1, html.Split("carousel.js", StringSplitOptions.None).Length - 1);
    }

    private static void AssertProductCardScriptLoadedOnce(string html)
    {
        Assert.Equal(1, html.Split("product-card.js", StringSplitOptions.None).Length - 1);
    }

    private sealed class Stub(CatalogPage page, CatalogResult related, ProductDetailResult detail, CatalogQuoteResult quote, CatalogFilters filters) : ICatalogClient
    {
        public static CatalogQuoteRequest? LastQuoteRequest { get; private set; }
        public static CatalogQuery? LastCatalogQuery { get; private set; }
        public Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default) => Task.FromResult(CatalogResult.Empty());
        public Task<CatalogPage> GetCatalogAsync(CatalogQuery query, CancellationToken cancellationToken = default) { LastCatalogQuery = query; return Task.FromResult(page); }
        public Task<CatalogFilters?> GetFiltersAsync(CancellationToken cancellationToken = default) => Task.FromResult<CatalogFilters?>(filters);
        public Task<ProductDetailResult> GetProductAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(detail);
        public Task<CatalogResult> GetRelatedAsync(string slug, int limit = 4, CancellationToken cancellationToken = default) => Task.FromResult(related);
        public Task<CatalogQuoteResult> QuoteAsync(CatalogQuoteRequest request, CancellationToken cancellationToken = default) { LastQuoteRequest = request; return Task.FromResult(quote); }
    }
}
