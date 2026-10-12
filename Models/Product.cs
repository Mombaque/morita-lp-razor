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
    public PublicCatalogAudience Audience { get; set; } = PublicCatalogAudience.Adult;
    public List<ProductVariant> Variants { get; set; } = [];
    public ProductSizeGuide? SizeGuide { get; set; }
}

public sealed record ProductSizeGuide(string? Title, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, string? Note)
{
    public const int MaximumColumns = 8;
    public const int MaximumRows = 30;

    public static ProductSizeGuide? Create(string? title, IReadOnlyList<string>? columns, IReadOnlyList<IReadOnlyList<string>?>? rows, string? note)
    {
        if (columns is null || rows is null || columns.Count is < 1 or > MaximumColumns || rows.Count is < 1 or > MaximumRows)
            return null;
        if (rows.Any(row => row is null || row.Count != columns.Count))
            return null;
        static string Clean(string? value) => (value ?? "").Trim();
        return new(
            string.IsNullOrWhiteSpace(title) ? null : Clean(title),
            columns.Select(Clean).ToList(),
            rows.Select(row => (IReadOnlyList<string>)row!.Select(Clean).ToList()).ToList(),
            string.IsNullOrWhiteSpace(note) ? null : Clean(note));
    }
}
