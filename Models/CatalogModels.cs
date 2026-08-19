namespace Morita.LP.Razor.Models;

public enum CatalogLoadState { Success, Partial, Empty, Unavailable }

public sealed record CatalogResult(CatalogLoadState State, IReadOnlyList<Product> Products)
{
    public static CatalogResult Success(IReadOnlyList<Product> products) => new(CatalogLoadState.Success, products);
    public static CatalogResult Empty() => new(CatalogLoadState.Empty, []);
    public static CatalogResult Unavailable() => new(CatalogLoadState.Unavailable, []);
}

public sealed class PublicCatalogProductResponse
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? FormattedPrice { get; set; }
    public List<PublicCatalogVariantResponse?>? ColorVariants { get; set; }
}

public sealed class PublicCatalogVariantResponse
{
    public List<string?>? Images { get; set; }
}

public sealed class CustomerProductRequestItem
{
    public string? ProductType { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public int? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public int? Age { get; set; }
    public string? Notes { get; set; }
}

public sealed class CustomerProductRequestPayload
{
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? Modality { get; set; }
    public string? Notes { get; set; }
    public string? Website { get; set; }
    public bool AcceptedPrivacyPolicy { get; set; }
    public string? Source { get; set; }
    public string? LandingPage { get; set; }
    public string? Campaign { get; set; }
    public List<CustomerProductRequestItem>? Items { get; set; } = [];
}
