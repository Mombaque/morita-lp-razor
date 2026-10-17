namespace Morita.LP.Razor.Configuration;

public static class ErrorTrackingConfiguration
{
    public const string Section = "Sentry";
    private const double DefaultTracesSampleRate = 0.05;

    public static void AddStorefrontErrorTracking(this IWebHostBuilder webHost, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(Section);
        var dsn = section["Dsn"];
        if (string.IsNullOrWhiteSpace(dsn))
            return;

        webHost.UseSentry(options =>
        {
            options.Dsn = dsn;
            options.Environment = section["Environment"] ?? environment.EnvironmentName;
            options.Release = section["Release"] ?? Environment.GetEnvironmentVariable("FLY_IMAGE_REF");
            options.SendDefaultPii = false;
            options.MaxRequestBodySize = Sentry.Extensibility.RequestSize.None;
            options.TracesSampleRate = section.GetValue("TracesSampleRate", DefaultTracesSampleRate);
        });
    }
}
