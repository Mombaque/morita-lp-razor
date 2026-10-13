using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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

public sealed class ReviewOptOutPageTests
{
    private static readonly string Token = new string('a', 43) + "." + new string('B', 43);

    [Fact]
    public async Task Opening_the_link_only_asks_for_confirmation()
    {
        var reviews = new OptOutReviews(ReviewOptOutState.Done);
        using var factory = Create(reviews);

        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync($"/avaliacoes/descadastrar?token={Token}")).Content.ReadAsStringAsync());

        Assert.Contains("Parar pedidos de avaliação", html);
        Assert.Contains("name=\"robots\" content=\"noindex,nofollow\"", html);
        Assert.Null(reviews.LastToken);
    }

    [Theory]
    [InlineData(ReviewOptOutState.Done, "Pronto. Você não vai mais receber e-mails pedindo a avaliação")]
    [InlineData(ReviewOptOutState.Invalid, "Este link está incompleto ou é inválido.")]
    [InlineData(ReviewOptOutState.Unavailable, "Não foi possível concluir agora.")]
    public async Task Confirming_calls_the_api_and_reports_the_outcome(ReviewOptOutState state, string message)
    {
        var reviews = new OptOutReviews(state);
        using var factory = Create(reviews);
        using var client = factory.CreateClient();
        var page = await (await client.GetAsync($"/avaliacoes/descadastrar?token={Token}")).Content.ReadAsStringAsync();
        var antiforgery = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

        var response = await client.PostAsync("/avaliacoes/descadastrar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiforgery,
            ["Token"] = Token
        }));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(Token, reviews.LastToken);
        Assert.Contains(message, html);
        Assert.Equal(state == ReviewOptOutState.Done, response.RequestMessage!.RequestUri!.Query.Contains("pronto=true"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?token=abc.def")]
    public async Task A_malformed_link_explains_without_calling_the_api(string query)
    {
        var reviews = new OptOutReviews(ReviewOptOutState.Done);
        using var factory = Create(reviews);

        var html = WebUtility.HtmlDecode(await (await factory.CreateClient().GetAsync($"/avaliacoes/descadastrar{query}")).Content.ReadAsStringAsync());

        Assert.Contains("Este link está incompleto ou é inválido.", html);
        Assert.DoesNotContain("Parar pedidos de avaliação</button>", html);
        Assert.Null(reviews.LastToken);
    }

    [Fact]
    public async Task Robots_keeps_the_opt_out_page_private()
    {
        using var factory = Create(new OptOutReviews(ReviewOptOutState.Done));

        var robots = await factory.CreateClient().GetStringAsync("/robots.txt");

        Assert.Contains("Disallow: /avaliacoes/descadastrar", robots);
    }

    private static WebApplicationFactory<Program> Create(OptOutReviews reviews) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IReviewClient>();
                services.AddScoped<IReviewClient>(_ => reviews);
            });
        });

    private sealed class OptOutReviews(ReviewOptOutState state) : IReviewClient
    {
        public string? LastToken { get; private set; }

        public Task<ReviewOptOutState> OptOutAsync(string token, CancellationToken cancellationToken = default)
        {
            LastToken = token;
            return Task.FromResult(state);
        }

        public Task<ReviewFormResult> GetFormAsync(string token, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReviewFormResult(ReviewLoadState.NotFound, null));

        public Task<ReviewSubmitResult> SubmitAsync(string token, IReadOnlyList<ReviewInput> items, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReviewSubmitResult(ReviewSubmitState.Unavailable, null));

        public Task<ProductReviewPage?> GetProductReviewsAsync(string slug, int page, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProductReviewPage?>(null);
    }
}
