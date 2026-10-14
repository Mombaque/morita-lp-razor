using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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

public sealed class PostalCodeRelayTests
{
    [Fact]
    public async Task Found_postal_code_returns_the_address_fields()
    {
        var checkout = new StubCheckout(new PostalCodeLookupResult(CheckoutLoadState.Success, new PostalCodeAddress("Rua Direita", "Centro", "Sorocaba", "SP")));
        using var factory = Create(checkout);

        var response = await factory.CreateClient().GetAsync("/address/postal-code/18010-000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Rua Direita", json.RootElement.GetProperty("street").GetString());
        Assert.Equal("Centro", json.RootElement.GetProperty("neighborhood").GetString());
        Assert.Equal("Sorocaba", json.RootElement.GetProperty("city").GetString());
        Assert.Equal("SP", json.RootElement.GetProperty("state").GetString());
        Assert.Equal("18010000", checkout.LastPostalCode);
    }

    [Theory]
    [InlineData(CheckoutLoadState.NotFound, HttpStatusCode.NotFound)]
    [InlineData(CheckoutLoadState.RateLimited, HttpStatusCode.TooManyRequests)]
    [InlineData(CheckoutLoadState.Unavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(CheckoutLoadState.Timeout, HttpStatusCode.ServiceUnavailable)]
    [InlineData(CheckoutLoadState.Malformed, HttpStatusCode.ServiceUnavailable)]
    public async Task Lookup_failures_map_to_status_codes(CheckoutLoadState state, HttpStatusCode expected)
    {
        using var factory = Create(new StubCheckout(PostalCodeLookupResult.Failure(state)));

        var response = await factory.CreateClient().GetAsync("/address/postal-code/18010000");

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("1801000")]
    [InlineData("180100000")]
    [InlineData("18010-0000")]
    [InlineData("18O10000")]
    public async Task Malformed_postal_code_is_rejected_without_calling_the_api(string postalCode)
    {
        var checkout = new StubCheckout(PostalCodeLookupResult.Failure(CheckoutLoadState.Unavailable));
        using var factory = Create(checkout);

        var response = await factory.CreateClient().GetAsync($"/address/postal-code/{postalCode}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(checkout.LastPostalCode);
    }

    private static WebApplicationFactory<Program> Create(StubCheckout checkout) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICheckoutClient>();
                services.AddScoped<ICheckoutClient>(_ => checkout);
            });
        });

    private sealed class StubCheckout(PostalCodeLookupResult result) : ICheckoutClient
    {
        public string? LastPostalCode { get; private set; }

        public Task<PostalCodeLookupResult> LookupPostalCodeAsync(string postalCode, CancellationToken cancellationToken = default)
        {
            LastPostalCode = postalCode;
            return Task.FromResult(result);
        }

        public Task<CheckoutConfigurationResult> GetConfigurationAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CheckoutResult> CreateAsync(CheckoutCreateRequest request, string idempotencyKey, string accessToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CheckoutResult> GetAsync(Guid publicCheckoutId, string accessToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CheckoutResult> CancelAsync(Guid publicCheckoutId, string accessToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
