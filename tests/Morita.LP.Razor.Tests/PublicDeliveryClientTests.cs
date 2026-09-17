using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Tests;

public sealed class PublicDeliveryClientTests
{
    [Fact]
    public async Task Adds_proxy_headers_only_for_valid_fly_client_ip_and_server_secret()
    {
        var handler = new CaptureHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var factory = new SingleClientFactory(handler);
        var options = Options.Create(new DeliveryTrackingOptions
        {
            ApiBaseUrl = "https://api.example",
            PublicDeliveryPath = "/v1/public/deliveries/{publicToken}",
            ProxySecret = "server-only"
        });
        var contextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        contextAccessor.HttpContext.Request.Headers["Fly-Client-IP"] = "203.0.113.7";
        var client = new PublicDeliveryClient(factory, options, contextAccessor);

        Assert.Null(await client.GetAsync("a-safe_token"));
        Assert.Equal("https://api.example/v1/public/deliveries/a-safe_token", handler.Request!.RequestUri!.ToString());
        Assert.Equal("203.0.113.7", handler.Request.Headers.GetValues("X-Morita-Client-IP").Single());
        Assert.Equal("server-only", handler.Request.Headers.GetValues("X-Morita-Proxy-Secret").Single());
    }

    [Fact]
    public async Task Does_not_forward_unvalidated_fly_header()
    {
        var handler = new CaptureHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var contextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        contextAccessor.HttpContext.Request.Headers["Fly-Client-IP"] = "not-an-ip";
        var client = new PublicDeliveryClient(new SingleClientFactory(handler), Options.Create(new DeliveryTrackingOptions { ProxySecret = "server-only" }), contextAccessor);

        await client.GetAsync("a-safe_token");

        Assert.False(handler.Request!.Headers.Contains("X-Morita-Client-IP"));
        Assert.False(handler.Request.Headers.Contains("X-Morita-Proxy-Secret"));
    }

    [Fact]
    public async Task Malformed_upstream_json_is_not_silently_accepted()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{not-json") };
        var client = new PublicDeliveryClient(new SingleClientFactory(new CaptureHandler(response)), Options.Create(new DeliveryTrackingOptions()), new HttpContextAccessor());

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => client.GetAsync("a-safe_token"));
    }

    [Fact]
    public async Task Upstream_error_status_is_exposed_as_http_failure()
    {
        var client = new PublicDeliveryClient(new SingleClientFactory(new CaptureHandler(new HttpResponseMessage(HttpStatusCode.BadGateway))), Options.Create(new DeliveryTrackingOptions()), new HttpContextAccessor());

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("a-safe_token"));
    }

    [Fact]
    public async Task Deserializes_the_expanded_public_delivery_contract()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "displayOrderNumber": "PED-123",
                  "status": "OutForDelivery",
                  "createdAt": "2026-08-25T10:00:00Z",
                  "statusUpdatedAt": "2026-08-25T11:00:00Z",
                  "estimatedDeliveryFrom": "2026-08-25T14:00:00Z",
                  "estimatedDeliveryTo": "2026-08-25T18:00:00Z",
                  "destinationDistrict": "Centro",
                  "destinationCity": "Sorocaba",
                  "items": [{ "name": "Luva", "quantity": 1, "size": "M", "color": "Preta" }]
                }
                """)
        };
        var client = new PublicDeliveryClient(new SingleClientFactory(new CaptureHandler(response)), Options.Create(new DeliveryTrackingOptions()), new HttpContextAccessor());

        var delivery = await client.GetAsync("a-safe_token");

        Assert.NotNull(delivery);
        Assert.Equal("PED-123", delivery.DisplayOrderNumber);
        Assert.Equal("Centro", delivery.DestinationDistrict);
        Assert.Equal("Sorocaba", delivery.DestinationCity);
        Assert.Equal("M", delivery.Items.Single().Size);
        Assert.Equal("Preta", delivery.Items.Single().Color);
    }

    [Theory]
    [InlineData("ftp://api.example", false)]
    [InlineData("https://api.example", true)]
    [InlineData("/relative", false)]
    public void Api_base_url_validation_requires_http_or_https(string value, bool expected)
    {
        Assert.Equal(expected, DeliveryTrackingOptions.IsValidApiBaseUrl(value));
    }

    [Theory]
    [InlineData("America/Sao_Paulo", true)]
    [InlineData("", false)]
    [InlineData("Not/A_TimeZone", false)]
    public void Time_zone_validation_rejects_invalid_values(string value, bool expected)
    {
        Assert.Equal(expected, DeliveryTrackingOptions.IsValidTimeZoneId(value));
    }

    [Theory]
    [InlineData("/v1/public/deliveries/{publicToken}", true)]
    [InlineData("https://api.example/v1/{publicToken}", false)]
    [InlineData("/v1/public/deliveries/{publicToken}/{publicToken}", false)]
    [InlineData("/v1/public/deliveries/without-token", false)]
    public void Public_path_validation_requires_one_relative_token_placeholder(string value, bool expected)
    {
        Assert.Equal(expected, DeliveryTrackingOptions.IsValidPublicDeliveryPath(value));
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("https://api.example") };
    }

    private sealed class CaptureHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(response);
        }
    }
}
