using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Morita.LP.Razor.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class OrderClientTests
{
    [Fact]
    public async Task Get_sends_order_token_and_maps_string_pickup_json()
    {
        var number = "MF-0123456789ABCDEF";
        using var handler = new Handler(Json(number));
        var result = await Create(handler).GetAsync(number, new string('t', 32));
        Assert.Equal(OrderLoadState.Success, result.State);
        Assert.Equal("https://api.test/v1/storefront/orders/MF-0123456789ABCDEF", handler.Request!.RequestUri!.ToString());
        Assert.Equal(new string('t', 32), handler.Request.Headers.GetValues("X-Order-Access-Token").Single());
        Assert.Equal("Rua A", result.Order!.PickupAddress.Street);
    }

    [Fact]
    public async Task Get_rejects_bad_status_totals_address_and_number()
    {
        var number = "MF-0123456789ABCDEF";
        Assert.Equal(OrderLoadState.Malformed, (await Create(new Handler(Json(number, payment: "unknown"))).GetAsync(number, Token)).State);
        Assert.Equal(OrderLoadState.Malformed, (await Create(new Handler(Json(number, amount: 11))).GetAsync(number, Token)).State);
        Assert.Equal(OrderLoadState.Malformed, (await Create(new Handler(Json(number, address: "{}"))).GetAsync(number, Token)).State);
        Assert.Equal(OrderLoadState.Malformed, (await Create(new Handler(Json("MF-0123456789ABCDEXI"))).GetAsync("MF-0123456789ABCDEXI", Token)).State);
    }

    [Fact]
    public async Task Get_maps_not_found_to_generic_unauthorized_and_timeout()
    {
        var notFound = await Create(new Handler(HttpStatusCode.NotFound)).GetAsync("MF-0123456789ABCDEF", Token);
        Assert.Equal(OrderLoadState.Unauthorized, notFound.State);
        var malformed = await Create(new Handler("not-json")).GetAsync("MF-0123456789ABCDEF", Token);
        Assert.Equal(OrderLoadState.Malformed, malformed.State);
    }

    private const string Token = "tttttttttttttttttttttttttttttttt";
    private static OrderClient Create(HttpMessageHandler handler) => new(new HttpClient(handler) { BaseAddress = new("https://api.test/") }, Options.Create(new CatalogApiOptions { BaseUrl = "https://api.test", TimeoutSeconds = 2 }), new HttpContextAccessor { HttpContext = new DefaultHttpContext() }, new TestEnvironment(), NullLogger<OrderClient>.Instance);
    private static string Json(string number, string payment = "Converted", decimal amount = 10, string address = "{\"street\":\"Rua A\",\"number\":\"1\",\"neighborhood\":\"Centro\",\"city\":\"Sorocaba\",\"state\":\"SP\",\"postalCode\":\"18000-000\"}") => $$$"""{"publicOrderNumber":"{{{number}}}","paymentStatus":"{{{payment}}}","fulfillmentStatus":"Pending","amount":{{{amount}}},"currency":"BRL","createdAt":"2026-08-20T12:00:00Z","pickupDisplayName":"Loja","pickupAddressJson":{{{JsonSerializer.Serialize(address)}}},"pickupHours":"09:00-18:00","pickupInstructions":"Documento","lines":[{"description":"Item","presentation":"Item","quantity":1,"unitPrice":10,"total":10}]}""";
    private sealed class Handler : HttpMessageHandler
    {
        private readonly string? body; private readonly HttpStatusCode status;
        public Handler(string body) { this.body = body; status = HttpStatusCode.OK; }
        public Handler(HttpStatusCode status) { this.status = status; }
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Request = request; return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body ?? "", System.Text.Encoding.UTF8, "application/json") }); }
    }
    private sealed class TestEnvironment : IHostEnvironment { public string EnvironmentName { get; set; } = Environments.Development; public string ApplicationName { get; set; } = "tests"; public string ApplicationVersion { get; set; } = "tests"; public string ContentRootPath { get; set; } = AppContext.BaseDirectory; public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider(); }
}
