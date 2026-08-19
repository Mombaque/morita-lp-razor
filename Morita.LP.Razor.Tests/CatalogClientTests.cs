using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class CatalogClientTests
{
    [Fact]
    public async Task Maps_successful_public_response_to_safe_local_fields()
    {
        var result = await Create(HttpStatusCode.OK, "[{\"name\":\"Kimono\",\"description\":\"Leve\",\"formattedPrice\":\"R$ 99,90\",\"colorVariants\":[{\"images\":[\"/img.jpg\"]}]}]").GetProductsAsync("jiu-jitsu");
        Assert.Equal(CatalogLoadState.Success, result.State);
        Assert.Equal("Kimono", result.Products[0].Nome);
        Assert.Equal("/img.jpg", result.Products[0].Imagens[0]);
        Assert.Equal("R$ 99,90", result.Products[0].FormattedPrice);
    }

    [Fact]
    public async Task Empty_not_found_malformed_and_timeout_are_unavailable_or_empty()
    {
        Assert.Equal(CatalogLoadState.Empty, (await Create(HttpStatusCode.OK, "[]").GetProductsAsync("muay-thai")).State);
        Assert.Equal(CatalogLoadState.Unavailable, (await Create(HttpStatusCode.NotFound, "").GetProductsAsync("muay-thai")).State);
        Assert.Equal(CatalogLoadState.Unavailable, (await Create(HttpStatusCode.OK, "not-json").GetProductsAsync("muay-thai")).State);
        Assert.Equal(CatalogLoadState.Unavailable, (await Create(HttpStatusCode.OK, "", delay: true).GetProductsAsync("muay-thai")).State);
        var nullFields = await Create(HttpStatusCode.OK, "[{\"name\":\"Sem imagem\",\"colorVariants\":null}]").GetProductsAsync("muay-thai");
        Assert.Equal(CatalogLoadState.Success, nullFields.State);
        Assert.Empty(nullFields.Products[0].Imagens);
        Assert.Equal(CatalogLoadState.Unavailable, (await Create(HttpStatusCode.OK, "[null]").GetProductsAsync("muay-thai")).State);
    }

    private static ICatalogClient Create(HttpStatusCode status, string content, bool delay = false)
    {
        var client = new HttpClient(new ControlledHandler(status, content, delay)) { BaseAddress = new Uri("https://catalog.test/") };
        return new CatalogClient(client, Options.Create(new CatalogApiOptions { TimeoutSeconds = 1 }), NullLogger<CatalogClient>.Instance);
    }

    private sealed class ControlledHandler(HttpStatusCode status, string content, bool delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (delay) await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(content) };
        }
    }
}
