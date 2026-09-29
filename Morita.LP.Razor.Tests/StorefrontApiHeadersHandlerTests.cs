using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class StorefrontApiHeadersHandlerTests
{
    [Fact]
    public async Task Forwards_the_normalized_store_host_with_the_proxy_secret_and_client_ip()
    {
        var context = ShopperContext("Loja.Example.COM.:8443");
        var recorder = new RecordingHandler();

        await Send(recorder, new CatalogApiOptions { ProxySecret = "proxy-secret" }, context);

        Assert.Equal("loja.example.com", recorder.Request!.Headers.GetValues(StorefrontApiHeadersHandler.StorefrontHostHeader).Single());
        Assert.Equal("proxy-secret", recorder.Request.Headers.GetValues(StorefrontApiHeadersHandler.ProxySecretHeader).Single());
        Assert.Equal("203.0.113.9", recorder.Request.Headers.GetValues(StorefrontApiHeadersHandler.ClientIpHeader).Single());
    }

    [Fact]
    public async Task Does_not_forward_the_store_host_without_a_proxy_secret()
    {
        var recorder = new RecordingHandler();

        await Send(recorder, new CatalogApiOptions(), ShopperContext("loja.example.com"));

        Assert.False(recorder.Request!.Headers.Contains(StorefrontApiHeadersHandler.StorefrontHostHeader));
        Assert.False(recorder.Request.Headers.Contains(StorefrontApiHeadersHandler.ProxySecretHeader));
        Assert.True(recorder.Request.Headers.Contains(StorefrontApiHeadersHandler.ClientIpHeader));
    }

    [Fact]
    public async Task Without_a_request_sends_only_the_proxy_secret()
    {
        var recorder = new RecordingHandler();

        await Send(recorder, new CatalogApiOptions { ProxySecret = "proxy-secret" }, context: null);

        Assert.False(recorder.Request!.Headers.Contains(StorefrontApiHeadersHandler.StorefrontHostHeader));
        Assert.False(recorder.Request.Headers.Contains(StorefrontApiHeadersHandler.ClientIpHeader));
        Assert.Equal("proxy-secret", recorder.Request.Headers.GetValues(StorefrontApiHeadersHandler.ProxySecretHeader).Single());
    }

    [Fact]
    public async Task Replaces_identity_headers_set_by_the_caller()
    {
        var recorder = new RecordingHandler();
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/storefront/catalog/filters");
        request.Headers.TryAddWithoutValidation(StorefrontApiHeadersHandler.StorefrontHostHeader, "other-store.example.com");
        request.Headers.TryAddWithoutValidation(StorefrontApiHeadersHandler.ClientIpHeader, "198.51.100.1");

        using var client = StorefrontApiTestPipeline.CreateClient(recorder, new CatalogApiOptions { ProxySecret = "proxy-secret" }, ShopperContext("loja.example.com"));
        using var _ = await client.SendAsync(request);

        Assert.Equal("loja.example.com", recorder.Request!.Headers.GetValues(StorefrontApiHeadersHandler.StorefrontHostHeader).Single());
        Assert.Equal("203.0.113.9", recorder.Request.Headers.GetValues(StorefrontApiHeadersHandler.ClientIpHeader).Single());
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("MORITAFIGHT.com.br", "moritafight.com.br")]
    [InlineData("www.moritafight.com.br:443", "www.moritafight.com.br")]
    [InlineData("[::1]:8080", "[::1]")]
    public void NormalizeHost_lowercases_and_drops_the_port(string host, string? expected) =>
        Assert.Equal(expected, StorefrontApiHeadersHandler.NormalizeHost(new HostString(host)));

    private static async Task Send(RecordingHandler recorder, CatalogApiOptions options, HttpContext? context)
    {
        using var client = StorefrontApiTestPipeline.CreateClient(recorder, options, context);
        using var _ = await client.GetAsync("v1/storefront/catalog/filters");
    }

    private static DefaultHttpContext ShopperContext(string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        return context;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
