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

public sealed class ProductReviewPageTests
{
    private const string Token = "MF-AAAAAAAAAAAAAAAA.abcdefghijklmnopqrstuvwxyz012345678901234";

    [Fact]
    public async Task Review_page_lists_open_products_and_submits_the_chosen_stars()
    {
        var reviews = new Reviews();
        using var factory = Create(reviews);
        using var client = factory.CreateClient();

        var body = await (await client.GetAsync($"/avaliar?token={Uri.EscapeDataString(Token)}")).Content.ReadAsStringAsync();
        Assert.Contains("data-review-item=\"kimono\"", body);
        Assert.Contains("Você já avaliou este produto.", body);
        Assert.Contains("name=\"Entries[0].Rating\"", body);
        Assert.DoesNotContain("name=\"Entries[1].Rating\"", body);

        var token = Regex.Match(body, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync($"/avaliar?token={Uri.EscapeDataString(Token)}", new FormUrlEncodedContent([
            new KeyValuePair<string, string>("Token", Token),
            new KeyValuePair<string, string>("Entries[0].Slug", "kimono"),
            new KeyValuePair<string, string>("Entries[0].Rating", "5"),
            new KeyValuePair<string, string>("Entries[0].Comment", "  Muito bom  "),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));
        var result = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-review-done", result);
        var submitted = Assert.Single(reviews.Submitted);
        Assert.Equal(new ReviewInput("kimono", 5, "Muito bom"), submitted);
    }

    [Fact]
    public async Task Review_page_rejects_malformed_tokens_without_calling_the_api()
    {
        var reviews = new Reviews();
        using var factory = Create(reviews);

        var body = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync("/avaliar?token=bad")).Content.ReadAsStringAsync());

        Assert.Contains("Este link de avaliação é inválido ou expirou.", body);
        Assert.Equal(0, reviews.FormCalls);
    }

    [Fact]
    public async Task Product_page_shows_rating_reviews_and_structured_data()
    {
        using var factory = Create(new Reviews());

        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync("/products/kimono")).Content.ReadAsStringAsync());

        Assert.Contains("data-detail-rating", html);
        Assert.Contains("4,5 (2 avaliações)", html);
        Assert.Contains("data-product-reviews", html);
        Assert.Contains("Resposta da Morita:", html);
        var jsonLd = Regex.Matches(html, "<script type=\"application/ld\\+json\">(.*?)</script>")
            .Select(match => JsonDocument.Parse(match.Groups[1].Value).RootElement)
            .Single(element => element.GetProperty("@type").GetString() == "Product");
        Assert.Equal(4.5m, jsonLd.GetProperty("aggregateRating").GetProperty("ratingValue").GetDecimal());
        Assert.Equal(2, jsonLd.GetProperty("aggregateRating").GetProperty("reviewCount").GetInt32());
        Assert.Equal("Ana S.", jsonLd.GetProperty("review")[0].GetProperty("author").GetProperty("name").GetString());
    }

    [Fact]
    public void Review_tokens_must_match_the_signed_format()
    {
        Assert.True(ReviewClient.IsValidToken(Token));
        Assert.False(ReviewClient.IsValidToken("MF-A.short"));
        Assert.False(ReviewClient.IsValidToken("mf-a.abcdefghijklmnopqrstuvwxyz012345678901234"));
        Assert.False(ReviewClient.IsValidToken(null));
    }

    private static WebApplicationFactory<Program> Create(Reviews reviews) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IReviewClient>();
                services.RemoveAll<ICatalogClient>();
                services.AddScoped<IReviewClient>(_ => reviews);
                services.AddScoped<ICatalogClient>(_ => new Catalog());
            });
        });

    private sealed class Reviews : IReviewClient
    {
        public List<ReviewInput> Submitted { get; } = [];
        public int FormCalls { get; private set; }

        public Task<ReviewFormResult> GetFormAsync(string token, CancellationToken cancellationToken = default)
        {
            FormCalls++;
            return Task.FromResult(new ReviewFormResult(ReviewLoadState.Success, new ReviewForm
            {
                PublicOrderNumber = "MF-AAAAAAAAAAAAAAAA",
                Items =
                [
                    new ReviewFormItem { Slug = "kimono", Name = "Kimono" },
                    new ReviewFormItem { Slug = "faixa", Name = "Faixa", Reviewed = true }
                ]
            }));
        }

        public Task<ReviewSubmitResult> SubmitAsync(string token, IReadOnlyList<ReviewInput> items, CancellationToken cancellationToken = default)
        {
            Submitted.AddRange(items);
            return Task.FromResult(new ReviewSubmitResult(ReviewSubmitState.Accepted, null));
        }

        public Task<ProductReviewPage?> GetProductReviewsAsync(string slug, int page, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProductReviewPage?>(new ProductReviewPage
            {
                Rating = new ProductRating { Average = 4.5m, Count = 2 },
                Items =
                [
                    new ProductReview { DisplayName = "Ana S.", Rating = 5, Comment = "Ótimo.", StaffReply = "Obrigado!", CreatedAt = DateTimeOffset.UtcNow },
                    new ProductReview { DisplayName = "Bruno", Rating = 4, Comment = "", CreatedAt = DateTimeOffset.UtcNow }
                ],
                Page = 1,
                TotalPages = 1
            });
    }

    private sealed class Catalog : ICatalogClient
    {
        public Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogResult.Empty());

        public Task<ProductDetailResult> GetProductAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProductDetailResult.Success(new Product
            {
                Slug = "kimono",
                Nome = "Kimono",
                Descricao = "Kimono trançado",
                Price = 300m,
                Availability = "available",
                Rating = new ProductRating { Average = 4.5m, Count = 2 },
                Imagens = ["/v1/storefront/catalog/images/3f2b1c1e-0000-4000-8000-000000000001"],
                Variants =
                [
                    new ProductVariant
                    {
                        ColorLabel = "Azul",
                        Images = ["/v1/storefront/catalog/images/3f2b1c1e-0000-4000-8000-000000000001"],
                        Offers = [new ProductOffer { PublicOfferId = Guid.NewGuid(), ColorLabel = "Azul", SizeLabel = "A1", UnitPrice = 300m, Currency = "BRL", Availability = "available" }]
                    }
                ]
            }));
    }
}
