namespace Morita.LP.Razor.Configuration;

public sealed class StorefrontOptions
{
    public const string SectionName = "Storefront";
    public string ProductSource { get; set; } = "Legacy";
    public bool UseRelayForCustomerRequests { get; set; }
    public string? DataProtectionKeyDirectory { get; set; }
}

public sealed class CatalogApiOptions
{
    public const string SectionName = "CatalogApi";
    public string BaseUrl { get; set; } = "https://morita-api.fly.dev";
    public int TimeoutSeconds { get; set; } = 5;
    public string? ProxySecret { get; set; }
}
