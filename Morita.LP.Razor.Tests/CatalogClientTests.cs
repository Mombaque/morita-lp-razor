using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
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

    [Fact]
    public async Task Catalog_reads_are_not_cached_by_default()
    {
        var handler = new CountingHandler(HttpStatusCode.OK, FiltersJson);
        var client = CreateCached(handler, cacheSeconds: 0, "loja.example.com");

        await client.GetFiltersAsync();
        await client.GetFiltersAsync();

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Cached_catalog_reads_reuse_successful_responses_per_storefront_host()
    {
        var handler = new CountingHandler(HttpStatusCode.OK, FiltersJson);
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("loja.example.com");
        var accessor = new HttpContextAccessor { HttpContext = context };
        var cache = new CatalogResponseCache(accessor, Options.Create(new CatalogApiOptions { CacheSeconds = 30 }));
        var client = new CatalogClient(new HttpClient(handler) { BaseAddress = new Uri("https://catalog.test/") }, Options.Create(new CatalogApiOptions { TimeoutSeconds = 1 }), NullLogger<CatalogClient>.Instance, cache);

        Assert.NotNull(await client.GetFiltersAsync());
        Assert.NotNull(await client.GetFiltersAsync());
        Assert.Equal(1, handler.Calls);

        context.Request.Host = new HostString("outra.example.com");
        Assert.NotNull(await client.GetFiltersAsync());
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Cached_catalog_reads_do_not_store_failures()
    {
        var handler = new CountingHandler(HttpStatusCode.InternalServerError, FiltersJson);
        var client = CreateCached(handler, cacheSeconds: 30, "loja.example.com");

        Assert.Null(await client.GetFiltersAsync());
        handler.Status = HttpStatusCode.OK;
        Assert.NotNull(await client.GetFiltersAsync());
        Assert.NotNull(await client.GetFiltersAsync());

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Quotes_are_never_cached()
    {
        var offer = Guid.NewGuid();
        var handler = new CountingHandler(HttpStatusCode.OK, $$"""{"currency":"BRL","total":10,"lines":[{"publicOfferId":"{{offer}}","quantity":1,"unitPrice":10,"linePrice":10,"currency":"BRL","availability":"available"}]}""");
        var client = CreateCached(handler, cacheSeconds: 30, "loja.example.com");
        var request = new CatalogQuoteRequest([new CatalogQuoteItem(offer, 1)]);

        Assert.Equal(CatalogLoadState.Success, (await client.QuoteAsync(request)).State);
        Assert.Equal(CatalogLoadState.Success, (await client.QuoteAsync(request)).State);

        Assert.Equal(2, handler.Calls);
    }

    private const string FiltersJson = "{}";

    private static ICatalogClient CreateCached(HttpMessageHandler handler, int cacheSeconds, string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        var cache = new CatalogResponseCache(new HttpContextAccessor { HttpContext = context }, Options.Create(new CatalogApiOptions { CacheSeconds = cacheSeconds }));
        return new CatalogClient(new HttpClient(handler) { BaseAddress = new Uri("https://catalog.test/") }, Options.Create(new CatalogApiOptions { TimeoutSeconds = 1 }), NullLogger<CatalogClient>.Instance, cache);
    }

    private sealed class CountingHandler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = status;
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(content) });
        }
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
