using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Morita.LP.Razor.Configuration;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class SecurityHeadersTests
{
    [Theory]
    [InlineData("/termos")]
    [InlineData("/cart")]
    [InlineData("/health")]
    [InlineData("/css/styles.css")]
    [InlineData("/pagina-que-nao-existe")]
    public async Task Every_response_carries_the_security_headers(string path)
    {
        using var factory = Create("E2E");

        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("SAMEORIGIN", Single(response, "X-Frame-Options"));
        Assert.Equal(SecurityHeaders.ReferrerPolicy, Single(response, "Referrer-Policy"));
        Assert.Equal(SecurityHeaders.PermissionsPolicy, Single(response, "Permissions-Policy"));
        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, Single(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task Https_responses_ask_browsers_to_keep_https_for_a_year()
    {
        using var factory = Create("E2E");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://moritafight.com.br") });

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("max-age=31536000", Single(response, "Strict-Transport-Security"));
    }

    private static string Single(HttpResponseMessage response, string name) =>
        Assert.Single(response.Headers.GetValues(name).ToArray());

    private static WebApplicationFactory<Program> Create(string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment(environment));
}
