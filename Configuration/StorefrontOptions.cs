namespace Morita.LP.Razor.Configuration;

public sealed class StorefrontOptions
{
    public const string SectionName = "Storefront";
    public string ProductSource { get; set; } = "Legacy";
    public bool PublicAssistantEnabled { get; set; }
    public bool CustomerAccountsEnabled { get; set; }
    public int PublicAssistantTimeoutSeconds { get; set; } = 25;
    public string? DataProtectionKeyDirectory { get; set; }
    public string? PrivacyPolicyUrl { get; set; }
    public string PrivacyPolicyVersion { get; set; } = "customer-account-v1";
}

public sealed class CatalogApiOptions
{
    public const string SectionName = "CatalogApi";
    public string BaseUrl { get; set; } = "https://morita-api.fly.dev";
    public int TimeoutSeconds { get; set; } = 5;
    // Seconds a successful catalog read is reused per storefront host; 0 disables the cache (Development, E2E).
    public int CacheSeconds { get; set; }
    public string? ProxySecret { get; set; }
}
