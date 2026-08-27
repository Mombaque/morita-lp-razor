using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public class JiuJitsuModel(ICatalogClient catalog) : PageModel
{
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public string? Category { get; set; }
    public CatalogPage Catalog { get; private set; } = new([], 1, CatalogQuery.PageSize, 0, 0, CatalogLoadState.Unavailable);
    public CatalogFilters Filters { get; private set; } = new();
    public Product? HeroProduct => Catalog.Items.FirstOrDefault(product => product.Imagens.Count > 0);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Category = NormalizeSlug(Category);
        var products = catalog.GetCatalogAsync(
            new CatalogQuery(null, null, null, null, null, null, true, 1, Category: Category, Modality: "jiu-jitsu"),
            cancellationToken);
        var filters = catalog.GetFiltersAsync(cancellationToken);
        await Task.WhenAll(products, filters);
        Catalog = await products;
        Filters = await filters ?? new();
    }

    private static string? NormalizeSlug(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant()[..Math.Min(80, value.Trim().Length)];
}
