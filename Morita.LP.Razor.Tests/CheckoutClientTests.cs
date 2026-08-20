using System.Net;
using System.Net.Http;
using System;
using System.Linq;
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

public sealed class CheckoutClientTests
{
    [Fact]
    public async Task Create_sends_both_credentials_and_rejects_bad_totals()
    {
        var offer = Guid.NewGuid();
        using var handler = new RecordingHandler("""{"publicCheckoutId":"11111111-1111-1111-1111-111111111111","status":"active","expiresAt":"2026-08-20T12:30:00Z","accessExpiresAt":"2026-09-19T12:00:00Z","lines":[{"publicOfferId":"00000000-0000-0000-0000-000000000001","quantity":1,"presentation":"Item","unitPrice":10,"lineTotal":9}],"merchandiseTotal":9,"discountTotal":0,"freightTotal":0,"total":9,"currency":"BRL","pickup":{"publicPickupId":"22222222-2222-2222-2222-222222222222","displayName":"Loja","address":{},"hours":"Dia","instructions":""},"contact":{"name":"N","email":"e@e.com","phone":"1"}}""");
        var client = Create(handler);
        var result = await client.CreateAsync(new([new(offer, 1)], new CheckoutContact { Name = "N", Email = "e@e.com", Phone = "1" }, Guid.NewGuid()), new string('i', 32), new string('a', 32));
        Assert.Equal(CheckoutLoadState.Malformed, result.State);
        Assert.Equal(new string('i', 32), handler.Request!.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal(new string('a', 32), handler.Request.Headers.GetValues("X-Checkout-Access-Token").Single());
        Assert.Equal("203.0.113.9", handler.Request.Headers.GetValues("X-Morita-Client-IP").Single());
        Assert.Equal("proxy-secret", handler.Request.Headers.GetValues("X-Morita-Proxy-Secret").Single());
    }

    [Fact]
    public async Task Validation_conflict_not_found_and_rate_limit_are_mapped()
    {
        foreach (var pair in new[] { (HttpStatusCode.UnprocessableEntity, CheckoutLoadState.Validation), (HttpStatusCode.Conflict, CheckoutLoadState.Conflict), (HttpStatusCode.NotFound, CheckoutLoadState.NotFound), ((HttpStatusCode)429, CheckoutLoadState.RateLimited) })
        {
            using var handler = new RecordingHandler(pair.Item1);
            var result = await Create(handler).GetAsync(Guid.NewGuid(), new string('a', 32));
            Assert.Equal(pair.Item2, result.State);
        }
    }

    [Fact]
    public async Task Create_maps_a_valid_authoritative_response_and_requires_the_requested_pickup()
    {
        var offer = Guid.NewGuid();
        var pickup = Guid.NewGuid();
        var body = $$$"""{"publicCheckoutId":"11111111-1111-1111-1111-111111111111","status":"active","expiresAt":"2026-08-20T12:30:00Z","accessExpiresAt":"2026-09-19T12:00:00Z","lines":[{"publicOfferId":"{{{offer}}}","quantity":1,"presentation":"Item","unitPrice":10,"lineTotal":10}],"merchandiseTotal":10,"discountTotal":0,"freightTotal":0,"total":10,"currency":"BRL","pickup":{"publicPickupId":"{{{pickup}}}","displayName":"Loja","address":{"street":"Rua A","number":"1","neighborhood":"Centro","city":"Sorocaba","state":"SP","postalCode":"18000-000"},"hours":"09:00-18:00","instructions":"Documento"},"contact":{"name":"Ana","email":"ana@example.com","phone":"+5515999999999"}}""";
        var result = await Create(new RecordingHandler(body)).CreateAsync(
            new([new(offer, 1)], new CheckoutContact { Name = "Ana", Email = "ana@example.com", Phone = "15999999999" }, pickup),
            new string('i', 32),
            new string('a', 32));

        Assert.Equal(CheckoutLoadState.Success, result.State);
        Assert.Equal(10, result.Checkout?.Total);

        var wrongPickupResult = await Create(new RecordingHandler(body)).CreateAsync(
            new([new(offer, 1)], new CheckoutContact { Name = "Ana", Email = "ana@example.com", Phone = "15999999999" }, Guid.NewGuid()),
            new string('i', 32),
            new string('a', 32));
        Assert.Equal(CheckoutLoadState.Malformed, wrongPickupResult.State);
    }

    [Fact]
    public async Task Cancel_maps_no_content_to_success()
    {
        var result = await Create(new RecordingHandler(HttpStatusCode.NoContent))
            .CancelAsync(Guid.NewGuid(), new string('a', 32));

        Assert.Equal(CheckoutLoadState.Success, result.State);
    }

    private static CheckoutClient Create(HttpMessageHandler handler)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        return new(
            new HttpClient(handler) { BaseAddress = new("https://api.test/") },
            Options.Create(new CatalogApiOptions { BaseUrl = "https://api.test", TimeoutSeconds = 2, ProxySecret = "proxy-secret" }),
            new HttpContextAccessor { HttpContext = context },
            new TestEnvironment(),
            NullLogger<CheckoutClient>.Instance);
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public RecordingHandler(HttpStatusCode status) : this("") { Status = status; }
        private HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ApplicationVersion { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
