namespace Morita.LP.Razor.Models;

public class ProductCardViewModel
{
    public required PublicCatalogProduct Product { get; init; }
    public required string TrackingCategory { get; init; }
    public required int Index { get; init; }
}
