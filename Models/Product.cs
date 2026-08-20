namespace Morita.LP.Razor.Models;

public class Product
{
    public string? Slug { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Alt { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public List<string> Imagens { get; set; } = new();
    public string? FormattedPrice { get; set; }
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
    public string Availability { get; set; } = "available";
    public string? FabricType { get; set; }
    public int? WeightGsm { get; set; }
    public List<string> Details { get; set; } = [];
    public CatalogLookup? Category { get; set; }
    public CatalogLookup? Modality { get; set; }
    public CatalogLookup? Brand { get; set; }
    public List<ProductVariant> Variants { get; set; } = [];
}
