using System;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Xunit;

namespace Morita.LP.Razor.Tests;

// Every WebApplicationFactory in this project boots the real Program, whose clients point at the production API
// (appsettings.json) or a developer's local API (appsettings.Development.json). Tests that do not replace a client,
// such as the store info read by the layout, would otherwise call those APIs. Before any test runs, point the
// default at a closed loopback port so such calls fail fast and locally; tests that need a base URL still set
// their own with UseSetting or ConfigureAppConfiguration, which take precedence over environment variables.
internal static class TestApiIsolation
{
    public const string UnreachableApi = "https://127.0.0.1:9";

    [ModuleInitializer]
    internal static void PointClientsAtAnUnreachableApi()
    {
        // Overrides any value inherited from the shell as well, so a developer's API settings never reach the tests.
        Environment.SetEnvironmentVariable("CatalogApi__BaseUrl", UnreachableApi);
    }
}

public sealed class TestApiIsolationTests
{
    [Theory]
    [InlineData("E2E")]
    [InlineData("Development")]
    public void Factories_without_their_own_api_point_at_the_unreachable_loopback_api(string environment)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment(environment));

        var options = factory.Services.GetRequiredService<IOptions<CatalogApiOptions>>().Value;

        Assert.Equal(TestApiIsolation.UnreachableApi, options.BaseUrl);
    }
}
