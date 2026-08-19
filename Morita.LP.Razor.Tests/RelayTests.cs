using System;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class RelayTests
{
    [Fact]
    public async Task Disabled_relay_returns_not_found()
    {
        using var factory = CreateFactory(false, new StubResponse(HttpStatusCode.OK, "{}"));
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/customer-product-request", Json("{}"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Relay_rejects_missing_antiforgery_token()
    {
        using var factory = CreateFactory(true, new StubResponse(HttpStatusCode.OK, "{}"));
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/customer-product-request", Json(ValidPayload));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Relay_rejects_null_items_without_throwing()
    {
        using var factory = CreateFactory(true, new StubResponse(HttpStatusCode.OK, "{}"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/customer-product-request")
        {
            Content = Json("{\"acceptedPrivacyPolicy\":true,\"items\":null}")
        };
        request.Headers.Add("RequestVerificationToken", await GetToken(client));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Relay_rejects_an_oversized_chunked_body_before_forwarding()
    {
        using var factory = CreateFactory(true, new StubResponse(HttpStatusCode.OK, "{}"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/customer-product-request")
        {
            Content = new ChunkedContent(new byte[(32 * 1024) + 1])
        };
        request.Headers.Add("RequestVerificationToken", await GetToken(client));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Relay_forwards_raw_payload_headers_and_success()
    {
        var handler = new RecordingHandler(new StubResponse(HttpStatusCode.OK, "{\"id\":7}"));
        using var factory = CreateFactory(true, handler);
        using var client = factory.CreateClient();
        var token = await GetToken(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/customer-product-request") { Content = Json(ValidPayload) };
        request.Headers.Add("RequestVerificationToken", token);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ValidPayload, handler.Body);
        Assert.Equal("relay-secret", handler.Request!.Headers.GetValues("X-Morita-Proxy-Secret").Single());
        Assert.Contains(handler.Request.Headers.GetValues("X-Morita-Client-IP").Single(), new[] { "127.0.0.1", "::1", "unknown" });
    }

    [Fact]
    public async Task Relay_passthroughs_api_400_and_maps_unavailable_or_timeout()
    {
        using (var badFactory = CreateFactory(true, new StubResponse(HttpStatusCode.BadRequest, "")))
        using (var client = badFactory.CreateClient())
        {
            var response = await PostWithToken(client);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var unavailableFactory = CreateFactory(true, new StubResponseException(new HttpRequestException())))
        using (var client = unavailableFactory.CreateClient())
            Assert.Equal(HttpStatusCode.BadGateway, (await PostWithToken(client)).StatusCode);

        using (var timeoutFactory = CreateFactory(true, new StubResponseException(new TaskCanceledException())))
        using (var client = timeoutFactory.CreateClient())
            Assert.Equal(HttpStatusCode.GatewayTimeout, (await PostWithToken(client)).StatusCode);
    }

    [Fact]
    public async Task Relay_rate_limit_rejects_the_thirteenth_request_with_429()
    {
        using var factory = CreateFactory(true, new StubResponse(HttpStatusCode.OK, "{}"));
        using var client = factory.CreateClient();
        var token = await GetToken(client);

        for (var index = 0; index < 12; index++)
        {
            using var accepted = await PostWithToken(client, token);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        using var rejected = await PostWithToken(client, token);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(bool enabled, IResponseHandler handler) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.UseSetting("Storefront:UseRelayForCustomerRequests", enabled.ToString());
            builder.UseSetting("CatalogApi:ProxySecret", "relay-secret");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(handler));
            });
        });

    private static async Task<string> GetToken(HttpClient client)
    {
        var body = await (await client.GetAsync("/")).Content.ReadAsStringAsync();
        return WebUtility.HtmlDecode(Regex.Match(body, "name=\"request-verification-token\" content=\"([^\"]+)\"").Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostWithToken(HttpClient client)
        => await PostWithToken(client, await GetToken(client));

    private static async Task<HttpResponseMessage> PostWithToken(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/customer-product-request") { Content = Json(ValidPayload) };
        request.Headers.Add("RequestVerificationToken", token);
        return await client.SendAsync(request);
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");
    private const string ValidPayload = "{\"customerName\":\"Maria\",\"acceptedPrivacyPolicy\":true,\"items\":[{\"productType\":\"Kimono\"}]}";

    private interface IResponseHandler
    {
        Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
    }

    private sealed record StubResponse(HttpStatusCode StatusCode, string Body) : IResponseHandler
    {
        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(StatusCode) { Content = new StringContent(Body) });
    }

    private sealed record StubResponseException(Exception Exception) : IResponseHandler
    {
        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(Exception);
    }

    private sealed class RecordingHandler(IResponseHandler response) : IResponseHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return await response.SendAsync(request, cancellationToken);
        }
    }

    private sealed class StubHttpClientFactory(IResponseHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(new DelegatingHandler(handler)) { BaseAddress = new Uri("https://api.test/") };
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class DelegatingHandler(IResponseHandler handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler.SendAsync(request, cancellationToken);
    }

    private sealed class ChunkedContent(byte[] content) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(content).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
