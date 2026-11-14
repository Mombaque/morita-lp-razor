using System;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Morita.LP.Razor.Tests;

// Each WebApplicationFactory host watches appsettings*.json for reloads and the web root for asp-append-version,
// and every watcher holds an inotify instance. Linux caps those per user (128 on GitHub runners), so a full run
// failed with "The configured user limit (128) on the number of inotify instances has been reached" and blocked
// the staging deploy. Tests never edit those files while running, so hosts skip config reloads and file providers
// poll instead of holding inotify instances.
internal static class TestHostFileWatchers
{
    [ModuleInitializer]
    internal static void AvoidInotifyWatchers()
    {
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");
    }
}

public sealed class TestHostFileWatchersTests
{
    [Fact]
    public void Test_hosts_do_not_reload_configuration_files()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("E2E"));

        var configuration = (IConfigurationRoot)factory.Services.GetRequiredService<IConfiguration>();

        Assert.All(configuration.Providers, provider => Assert.False(provider is FileConfigurationProvider { Source.ReloadOnChange: true }, $"{provider} reloads on change"));
    }
}
