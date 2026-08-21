using System.Net;
using System.Net.Http;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
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

    [Fact]
    public async Task Pix_initiation_sends_route_body_and_headers()
    {
        var id = Guid.NewGuid();
        var handler = new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10)));
        var result = await Create(handler).InitiatePixAsync(id, new string('a', 32), new string('i', 32));
        Assert.Equal(PaymentLoadState.Success, result.State);
        Assert.Equal($"https://api.test/v1/storefront/checkouts/{id:D}/payments/pix", handler.Request!.RequestUri!.ToString());
        Assert.Equal(new string('a', 32), handler.Request.Headers.GetValues("X-Checkout-Access-Token").Single());
        Assert.Equal(new string('i', 32), handler.Request.Headers.GetValues("Idempotency-Key").Single());
        Assert.Contains("\"method\":\"pix\"", handler.Body);
    }

    [Fact]
    public async Task Payment_get_and_cancel_use_protected_routes()
    {
        var id = Guid.NewGuid();
        using var getHandler = new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10)));
        await Create(getHandler).GetPaymentAsync(id, new string('a', 32));
        Assert.Equal($"https://api.test/v1/storefront/checkouts/{id:D}/payment", getHandler.Request!.RequestUri!.ToString());
        using var cancelHandler = new RecordingHandler(HttpStatusCode.NoContent);
        var result = await Create(cancelHandler).CancelPaymentAsync(id, new string('a', 32));
        Assert.Equal(PaymentLoadState.Success, result.State);
        Assert.Equal($"https://api.test/v1/storefront/checkouts/{id:D}/payment/cancel", cancelHandler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Pix_terminal_response_may_be_expired_but_converted_requires_public_number()
    {
        var expired = await Create(new RecordingHandler(PaymentJson("failed", DateTimeOffset.UtcNow.AddDays(-1)))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Success, expired.State);
        var converted = await Create(new RecordingHandler(PaymentJson("converted", DateTimeOffset.UtcNow.AddDays(-1), "MF-0123456789ABCDEF"))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Success, converted.State);
        var invalid = await Create(new RecordingHandler(PaymentJson("converted", DateTimeOffset.UtcNow.AddDays(-1)))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Malformed, invalid.State);
    }

    [Fact]
    public async Task Pix_rejects_bad_qr_and_status()
    {
        var badQr = await Create(new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10), null, "bm90LXBuZw=="))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Malformed, badQr.State);
        var missingPendingPix = await Create(new RecordingHandler(PaymentJson("pending", DateTimeOffset.UtcNow.AddMinutes(10), includePix: false))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Malformed, missingPendingPix.State);
        var processingWithoutPix = await Create(new RecordingHandler(PaymentJson("conversionpending", DateTimeOffset.UtcNow.AddMinutes(10), includePix: false))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Success, processingWithoutPix.State);
        var cancellationPending = await Create(new RecordingHandler(PaymentJson("CancellationPending", DateTimeOffset.UtcNow.AddMinutes(10), includePix: false))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Success, cancellationPending.State);
        Assert.Equal("cancellationpending", cancellationPending.Payment!.Status);
        var badStatus = await Create(new RecordingHandler(PaymentJson("unknown", DateTimeOffset.UtcNow.AddMinutes(10)))).GetPaymentAsync(Guid.NewGuid(), new string('a', 32));
        Assert.Equal(PaymentLoadState.Malformed, badStatus.State);
    }

    [Fact]
    public async Task Checkout_accepts_every_backend_payment_lifecycle_status_and_rejects_unknown()
    {
        foreach (var status in new[] { "Active", "Cancelled", "Expired", "PaymentPending", "ConversionPending", "RefundPending", "Completed", "Refunded" })
        {
            var result = await Create(new RecordingHandler(CheckoutJson(status))).GetAsync(Guid.NewGuid(), new string('a', 32));
            Assert.Equal(CheckoutLoadState.Success, result.State);
            Assert.Equal(status.ToLowerInvariant(), result.Checkout!.Status);
        }
        Assert.Equal(CheckoutLoadState.Malformed, (await Create(new RecordingHandler(CheckoutJson("Unknown"))).GetAsync(Guid.NewGuid(), new string('a', 32))).State);
    }

    private static string PaymentJson(string status, DateTimeOffset expires, string? order = null, string? qr = null, bool includePix = true) => JsonSerializer.Serialize(new { status, amount = 10.00m, currency = "BRL", expiresAt = expires, pixCopyPaste = includePix ? "000201010212" : null, qrCodePngBase64 = includePix ? qr ?? PngBase64 : null, publicOrderNumber = order });
    private static string CheckoutJson(string status) => JsonSerializer.Serialize(new { publicCheckoutId = Guid.Parse("11111111-1111-1111-1111-111111111111"), status, expiresAt = DateTimeOffset.UtcNow.AddHours(1), accessExpiresAt = DateTimeOffset.UtcNow.AddDays(30), lines = new[] { new { publicOfferId = Guid.Parse("22222222-2222-2222-2222-222222222222"), quantity = 1, presentation = "Item", unitPrice = 10m, lineTotal = 10m } }, merchandiseTotal = 10m, discountTotal = 0m, freightTotal = 0m, total = 10m, currency = "BRL", pickup = new { publicPickupId = Guid.Parse("33333333-3333-3333-3333-333333333333"), displayName = "Loja", address = new { street = "Rua", number = "1", neighborhood = "Centro", city = "Sorocaba", state = "SP", postalCode = "18000-000" }, hours = "09:00", instructions = "" }, contact = new { name = "Ana", email = "ana@example.com", phone = "1" } });
    private const string PngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAElEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

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
        public string Body { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
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
