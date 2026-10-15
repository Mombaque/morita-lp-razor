using System;
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

public sealed class ProductLowStockTests
{
    private static readonly Guid LowOffer = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid PlentyOffer = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public async Task Product_page_marks_only_low_stock_sizes()
    {
        using var factory = Create();

        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync("/products/kimono")).Content.ReadAsStringAsync());

        Assert.Contains("aria-label=\"Azul / A1, últimas unidades\"", html);
        Assert.Contains("aria-label=\"Azul / A2\"", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "class=\"offer-low-stock\""));
        Assert.Contains($"value=\"{LowOffer}\" data-offer-id=\"{LowOffer}\"", html);
        Assert.Contains("data-low-stock=\"true\"", html);
        Assert.Contains("aria-label=\"Azul / A1, últimas unidades\" data-presentation=\"Azul / A1\"", html);
    }

    [Fact]
    public async Task Selected_low_stock_offer_announces_last_units()
    {
        using var factory = Create();

        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync($"/products/kimono?publicOfferId={LowOffer}")).Content.ReadAsStringAsync());

        Assert.Contains("data-live-presentation>Azul / A1</span>", html);
        Assert.Contains("class=\"live-offer-badge is-low-stock\" data-live-availability>Últimas unidades</span>", html);
    }

    private static WebApplicationFactory<Program> Create() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICatalogClient>();
                services.AddScoped<ICatalogClient>(_ => new Catalog());
            });
        });

    private sealed class Catalog : ICatalogClient
    {
        public Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogResult.Empty());

        public Task<CatalogPage> GetCatalogAsync(CatalogQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogPage([], 1, CatalogQuery.PageSize, 0, 0, CatalogLoadState.Empty));

        public Task<ProductDetailResult> GetProductAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProductDetailResult.Success(new Product
            {
                Slug = "kimono",
                Nome = "Kimono",
                Descricao = "Kimono trançado",
                Price = 300m,
                Availability = "available",
                Imagens = ["/v1/storefront/catalog/images/3f2b1c1e-0000-4000-8000-000000000001"],
                Variants =
                [
                    new ProductVariant
                    {
                        ColorLabel = "Azul",
                        Images = ["/v1/storefront/catalog/images/3f2b1c1e-0000-4000-8000-000000000001"],
                        Offers =
                        [
                            new ProductOffer { PublicOfferId = LowOffer, ColorLabel = "Azul", SizeLabel = "A1", UnitPrice = 300m, Currency = "BRL", Availability = "available", LowStock = true },
                            new ProductOffer { PublicOfferId = PlentyOffer, ColorLabel = "Azul", SizeLabel = "A2", UnitPrice = 300m, Currency = "BRL", Availability = "available" }
                        ]
                    }
                ]
            }));
    }
}
