namespace Morita.LP.Razor.Models;

public enum CatalogLoadState { Success, Partial, Empty, Unavailable, NotFound }

public sealed record CatalogResult(CatalogLoadState State, IReadOnlyList<Product> Products)
{
    public static CatalogResult Success(IReadOnlyList<Product> products) => new(CatalogLoadState.Success, products);
    public static CatalogResult Empty() => new(CatalogLoadState.Empty, []);
    public static CatalogResult Unavailable() => new(CatalogLoadState.Unavailable, []);
    public static CatalogResult NotFound() => new(CatalogLoadState.NotFound, []);
}

public sealed record CatalogLookup(int Id, string? Slug, string Label);

public sealed class ProductVariant
{
    public int? ColorId { get; set; }
    public string? ColorLabel { get; set; }
    public List<string> Images { get; set; } = [];
    public List<ProductOffer> Offers { get; set; } = [];
}

public sealed class ProductOffer
{
    public Guid PublicOfferId { get; set; }
    public int? SizeId { get; set; }
    public string? SizeLabel { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? Currency { get; set; }
    public string Availability { get; set; } = "available";
    public int? ColorId { get; set; }
    public string? ColorLabel { get; set; }
}

public sealed class CatalogFilter
{
    public int Id { get; set; }
    public string? Slug { get; set; }
    public string Label { get; set; } = "";
}

public sealed class CatalogFilters
{
    public List<CatalogFilter> Categories { get; set; } = [];
    public List<CatalogFilter> Modalities { get; set; } = [];
    public List<CatalogFilter> Brands { get; set; } = [];
    public List<CatalogFilter> Sizes { get; set; } = [];
    public List<CatalogFilter> Colors { get; set; } = [];
}

public sealed record CatalogQuery(
    string? Search,
    int? CategoryId,
    int? ModalityId,
    int? BrandId,
    int? SizeId,
    int? ColorId,
    bool? Available,
    int Page,
    string Sort = "featured")
{
    public const int PageSize = 24;
}

public sealed record CatalogPage(IReadOnlyList<Product> Items, int Page, int PageSize, int TotalCount, int TotalPages, CatalogLoadState State);

public sealed record ProductDetailResult(CatalogLoadState State, Product? Product)
{
    public static ProductDetailResult Success(Product product) => new(CatalogLoadState.Success, product);
    public static ProductDetailResult Unavailable() => new(CatalogLoadState.Unavailable, null);
    public static ProductDetailResult NotFound() => new(CatalogLoadState.NotFound, null);
}

public sealed record CatalogQuoteItem(Guid PublicOfferId, int Quantity);
public sealed record CatalogQuoteRequest(IReadOnlyList<CatalogQuoteItem> Lines);
public sealed class CatalogQuoteLine
{
    public Guid PublicOfferId { get; set; }
    public int Quantity { get; set; }
    public string? Slug { get; set; }
    public string? Presentation { get; set; }
    public int? ColorId { get; set; }
    public string? ColorLabel { get; set; }
    public int? SizeId { get; set; }
    public string? SizeLabel { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? LinePrice { get; set; }
    public string? Currency { get; set; }
    public string? Availability { get; set; }
    public string? ImageUrl { get; set; }
}

public sealed record CatalogQuoteResult(CatalogLoadState State, string? Currency, decimal? Total, IReadOnlyList<CatalogQuoteLine> Lines)
{
    public static CatalogQuoteResult Unavailable() => new(CatalogLoadState.Unavailable, null, null, []);
    public static CatalogQuoteResult Success(string currency, decimal total, IReadOnlyList<CatalogQuoteLine> lines) => new(lines.Any(x => !string.Equals(x.Availability, "available", StringComparison.OrdinalIgnoreCase)) ? CatalogLoadState.Partial : CatalogLoadState.Success, currency, total, lines);
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
