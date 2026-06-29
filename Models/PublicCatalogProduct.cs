namespace Morita.LP.Razor.Models;

public class PublicCatalogProduct
{
    public string Slug { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Modality { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;
    public string? FabricType { get; set; }
    public int? WeightGsm { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string> AvailableColors { get; set; } = [];
    public List<string> AvailableSizes { get; set; } = [];
    public List<string> Details { get; set; } = [];
    public List<PublicCatalogProductVariant> ColorVariants { get; set; } = [];

    public string DisplayPrice => string.IsNullOrWhiteSpace(FormattedPrice) ? Price.ToString("C") : FormattedPrice;

    public List<string> PrimaryImages => ColorVariants
        .FirstOrDefault(variant => variant.Images.Count > 0)?.Images ?? [];
}

public class PublicCatalogProductVariant
{
    public string Color { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;
    public List<string> Images { get; set; } = [];

    public string DisplayPrice => string.IsNullOrWhiteSpace(FormattedPrice) ? Price.ToString("C") : FormattedPrice;
}
