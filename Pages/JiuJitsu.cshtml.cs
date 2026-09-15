using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public class JiuJitsuModel(ICatalogClient catalog) : PageModel
{
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public string? Category { get; set; }
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public int? BrandId { get; set; }
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "featured";
    public CatalogPage Catalog { get; private set; } = new([], 1, CatalogQuery.PageSize, 0, 0, CatalogLoadState.Unavailable);
    public CatalogFilters Filters { get; private set; } = new();
    public CatalogSectionViewModel Section => new()
    {
        Route = "/jiu-jitsu",
        Title = "Jiu-Jitsu",
        Eyebrow = "Grappling / Arte suave",
        Description = "Kimonos, rashguards e faixas para quem começa, evolui e compete.",
        CatalogEyebrow = "Estoque online",
        DefaultProductHeading = "Todo o Jiu-Jitsu",
        EmptyDescription = "Esta seleção ainda não tem itens publicados. Veja todo o Jiu-Jitsu ou fale com a equipe.",
        CardCategory = "jiu-jitsu",
        Category = Category,
        BrandId = BrandId,
        Sort = Sort,
        Catalog = Catalog,
        Filters = Filters
    };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Category = NormalizeSlug(Category);
        BrandId = BrandId is > 0 ? BrandId : null;
        Sort = CatalogSectionViewModel.NormalizeSort(Sort);
        var products = catalog.GetCatalogAsync(
            new CatalogQuery(null, null, null, BrandId, null, null, true, 1, Sort, Category: Category, Modality: "jiu-jitsu"),
            cancellationToken);
        var filters = catalog.GetFiltersAsync(cancellationToken);
        await Task.WhenAll(products, filters);
        Catalog = await products;
        Filters = await filters ?? new();
    }

    private static string? NormalizeSlug(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant()[..Math.Min(80, value.Trim().Length)];
}
