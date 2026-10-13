using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class SeoDocumentsTests
{
    private static readonly XNamespace Sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private static readonly XNamespace Google = "http://base.google.com/ns/1.0";

    [Fact]
    public async Task Robots_keeps_private_flows_out_and_points_to_the_sitemap()
    {
        using var factory = Create(new PagedCatalog([]));
        var response = await factory.CreateClient().GetAsync("/robots.txt");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("User-agent: *", body);
        Assert.Contains("Disallow: /cart", body);
        Assert.Contains("Disallow: /checkout", body);
        Assert.Contains("Disallow: /conta", body);
        Assert.Contains("Disallow: /order/", body);
        Assert.Contains("Disallow: /*?handler=", body);
        Assert.DoesNotContain("Disallow: /products", body);
        Assert.Contains("Sitemap: https://moritafight.com.br/sitemap.xml", body);
    }

    [Fact]
    public async Task Sitemap_lists_public_pages_and_every_catalog_product_across_pages()
    {
        var pages = new List<IReadOnlyList<Product>>
        {
            new[] { new Product { Slug = "kimono-azul", Nome = "Kimono" }, new Product { Slug = "luva 16oz", Nome = "Luva" } },
            new[] { new Product { Slug = "faixa", Nome = "Faixa" }, new Product { Slug = "kimono-azul", Nome = "Kimono" }, new Product { Nome = "Sem slug" } }
        };
        var catalog = new PagedCatalog(pages);
        using var factory = Create(catalog);
        var response = await factory.CreateClient().GetAsync("/sitemap.xml");
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var urls = document.Root!.Elements(Sitemap + "url").Select(url => url.Element(Sitemap + "loc")!.Value).ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
        [
            "https://moritafight.com.br/",
            "https://moritafight.com.br/jiu-jitsu",
            "https://moritafight.com.br/muay-thai",
            "https://moritafight.com.br/kids",
            "https://moritafight.com.br/products",
            "https://moritafight.com.br/products/kimono-azul",
            "https://moritafight.com.br/products/luva%2016oz",
            "https://moritafight.com.br/products/faixa"
        ], urls);
        Assert.Equal([1, 2], catalog.RequestedPages);
        Assert.DoesNotContain(urls, url => url.Contains("/cart") || url.Contains("/checkout") || url.Contains("/conta") || url.Contains("/order"));
    }

    [Fact]
    public async Task Sitemap_is_unavailable_instead_of_empty_when_the_catalog_cannot_be_read()
    {
        using var factory = Create(new PagedCatalog([], unavailable: true));
        var response = await factory.CreateClient().GetAsync("/sitemap.xml");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Product_page_publishes_rich_product_data_and_absolute_open_graph_image()
    {
        var offer = Guid.NewGuid();
        var product = new Product
        {
            Slug = "kimono",
            Nome = "Kimono Azul",
            Descricao = "Kimono trançado",
            Brand = new CatalogLookup(1, "morita", "Morita"),
            Category = new CatalogLookup(2, "kimonos", "Kimonos"),
            Imagens = ["/v1/storefront/catalog/images/3f2b1c1e-0000-4000-8000-000000000001"],
            Variants =
            [
                new ProductVariant
                {
                    ColorLabel = "Azul",
                    Images = ["/v1/storefront/catalog/images/3f2b1c1e-0000-4000-8000-000000000001"],
                    Offers = [new ProductOffer { PublicOfferId = offer, ColorLabel = "Azul", SizeLabel = "A2", UnitPrice = 399.9m, Availability = "available" }]
                }
            ]
        };
        using var factory = Create(new PagedCatalog([]), product);
        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync("/products/kimono")).Content.ReadAsStringAsync());

        Assert.Contains("<meta property=\"og:type\" content=\"product\">", html);
        Assert.Contains("<meta property=\"og:title\" content=\"Kimono Azul | Morita Fight\">", html);
        Assert.Contains("<meta property=\"og:image\" content=\"https://moritafight.com.br/v1/storefront/catalog/images/3f2b1c1e-0000-4000-8000-000000000001\">", html);
        var jsonLd = System.Text.RegularExpressions.Regex.Matches(html, "<script type=\"application/ld\\+json\">(.*?)</script>")
            .Select(match => JsonDocument.Parse(match.Groups[1].Value).RootElement)
            .Single(element => element.GetProperty("@type").GetString() == "Product");
        Assert.Equal("Morita", jsonLd.GetProperty("brand").GetProperty("name").GetString());
        Assert.Equal("Kimonos", jsonLd.GetProperty("category").GetString());
        Assert.Equal("https://moritafight.com.br/v1/storefront/catalog/images/3f2b1c1e-0000-4000-8000-000000000001", jsonLd.GetProperty("image")[0].GetString());
        var jsonOffer = jsonLd.GetProperty("offers")[0];
        Assert.Equal(offer.ToString(), jsonOffer.GetProperty("sku").GetString());
        Assert.Equal("Azul / A2", jsonOffer.GetProperty("name").GetString());
        Assert.Equal($"https://moritafight.com.br/products/kimono?publicOfferId={offer}", jsonOffer.GetProperty("url").GetString());
        Assert.Equal("https://schema.org/NewCondition", jsonOffer.GetProperty("itemCondition").GetString());
        Assert.Equal("Morita Fight", jsonOffer.GetProperty("seller").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Merchant_feed_lists_one_item_per_offer_with_google_fields()
    {
        var available = Guid.NewGuid();
        var soldOut = Guid.NewGuid();
        var image = "/v1/storefront/catalog/images/3f2b1c1e-0000-4000-8000-000000000001";
        var kimono = new Product
        {
            Slug = "kimono",
            Nome = "Kimono Infantil",
            Descricao = "Kimono trançado leve.",
            Brand = new CatalogLookup(1, "keiko", "Keiko"),
            Modality = new CatalogLookup(1, "jiu-jitsu", "Jiu-Jitsu"),
            Category = new CatalogLookup(2, "kimonos", "Kimonos"),
            Audience = PublicCatalogAudience.Kids,
            Price = 299.9m,
            Imagens = [image, "/v1/storefront/catalog/images/3f2b1c1e-0000-4000-8000-000000000002"],
            Variants =
            [
                new ProductVariant
                {
                    ColorLabel = "Branco",
                    Images = [image, "/v1/storefront/catalog/images/3f2b1c1e-0000-4000-8000-000000000002"],
                    Offers =
                    [
                        new ProductOffer { PublicOfferId = available, SizeLabel = "M0", UnitPrice = 299.9m, Currency = "BRL", Availability = "available" },
                        new ProductOffer { PublicOfferId = soldOut, SizeLabel = "M1", UnitPrice = 309m, Currency = "BRL", Availability = "unavailable" }
                    ]
                }
            ]
        };
        var withoutImage = new Product
        {
            Slug = "sem-foto",
            Nome = "Sem foto",
            Variants = [new ProductVariant { Offers = [new ProductOffer { PublicOfferId = Guid.NewGuid(), UnitPrice = 10m }] }]
        };
        using var factory = Create(new PagedCatalog([new[] { kimono, withoutImage }]));

        var response = await factory.CreateClient().GetAsync("/merchant-feed.xml");
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.Root!.Element("channel")!.Elements("item").ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2, items.Count);
        var first = items[0];
        string Field(XElement item, string name) => item.Element(Google + name)!.Value;
        Assert.Equal(available.ToString(), Field(first, "id"));
        Assert.Equal("kimono", Field(first, "item_group_id"));
        Assert.Equal("Kimono Infantil - Branco - M0", Field(first, "title"));
        Assert.Equal($"https://moritafight.com.br/products/kimono?publicOfferId={available}", Field(first, "link"));
        Assert.Equal($"https://moritafight.com.br{image}", Field(first, "image_link"));
        Assert.Single(first.Elements(Google + "additional_image_link"));
        Assert.Equal("in_stock", Field(first, "availability"));
        Assert.Equal("299.90 BRL", Field(first, "price"));
        Assert.Equal("new", Field(first, "condition"));
        Assert.Equal("Keiko", Field(first, "brand"));
        Assert.Equal("no", Field(first, "identifier_exists"));
        Assert.Equal("Branco", Field(first, "color"));
        Assert.Equal("M0", Field(first, "size"));
        Assert.Equal("kids", Field(first, "age_group"));
        Assert.Equal("Jiu-Jitsu > Kimonos", Field(first, "product_type"));
        Assert.Equal("out_of_stock", Field(items[1], "availability"));
        Assert.Equal("309.00 BRL", Field(items[1], "price"));
    }

    [Fact]
    public async Task Merchant_feed_is_unavailable_instead_of_empty_when_the_catalog_cannot_be_read()
    {
        using var factory = Create(new PagedCatalog([], unavailable: true));
        var response = await factory.CreateClient().GetAsync("/merchant-feed.xml");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static WebApplicationFactory<Program> Create(PagedCatalog catalog, Product? detail = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.UseSetting("CanonicalBaseUrl", "https://moritafight.com.br/");
            builder.ConfigureTestServices(services =>
            {
                catalog.Detail = detail;
                services.RemoveAll<ICatalogClient>();
                services.AddScoped<ICatalogClient>(_ => catalog);
            });
        });

    private sealed class PagedCatalog(IReadOnlyList<IReadOnlyList<Product>> pages, bool unavailable = false) : ICatalogClient
    {
        public List<int> RequestedPages { get; } = [];
        public Product? Detail { get; set; }

        public Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogResult.Empty());

        public Task<CatalogPage> GetCatalogAsync(CatalogQuery query, CancellationToken cancellationToken = default)
        {
            RequestedPages.Add(query.Page);
            if (unavailable)
                return Task.FromResult(new CatalogPage([], query.Page, CatalogQuery.PageSize, 0, 0, CatalogLoadState.Unavailable));
            var items = query.Page <= pages.Count ? pages[query.Page - 1] : [];
            var total = pages.Sum(page => page.Count);
            return Task.FromResult(new CatalogPage(items, query.Page, CatalogQuery.PageSize, total, pages.Count, items.Count == 0 ? CatalogLoadState.Empty : CatalogLoadState.Success));
        }

        public Task<ProductDetailResult> GetProductAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(Detail is null ? ProductDetailResult.NotFound() : ProductDetailResult.Success(Detail));
    }
}
