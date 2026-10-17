using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Sentry;
using Sentry.AspNetCore;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class ErrorTrackingConfigurationTests
{
    [Fact]
    public void Without_a_dsn_sentry_is_not_registered()
    {
        using var app = Build(new Dictionary<string, string?>());

        Assert.Null(app.Services.GetService<IHub>());
    }

    [Fact]
    public void With_a_dsn_sentry_is_registered_without_pii()
    {
        using var app = Build(new Dictionary<string, string?> { ["Sentry:Dsn"] = "https://public@o0.ingest.sentry.io/0" });

        Assert.NotNull(app.Services.GetService<IHub>());
        var options = app.Services.GetRequiredService<IOptions<SentryAspNetCoreOptions>>().Value;
        Assert.False(options.SendDefaultPii);
        Assert.Equal(0.05, options.TracesSampleRate);
    }

    private static WebApplication Build(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.AddStorefrontErrorTracking(builder.Configuration, builder.Environment);
        return builder.Build();
    }
}
