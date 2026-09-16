using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public class KidsModel(ICatalogClient catalog) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }
    [BindProperty(SupportsGet = true)]
    public string? Modality { get; set; }
    [BindProperty(SupportsGet = true)]
    public int? BrandId { get; set; }
    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "featured";
    public CatalogPage Catalog { get; private set; } = new([], 1, CatalogQuery.PageSize, 0, 0, CatalogLoadState.Unavailable);
    public CatalogFilters Filters { get; private set; } = new();
    public CatalogSectionViewModel Section => new()
    {
        Route = "/kids",
        Title = "Infantil",
        Eyebrow = "Jovens atletas / Grandes começos",
        Description = "Equipamento infantil separado de verdade: proporções, tamanhos e opções pensadas para crianças treinarem com conforto.",
        CatalogEyebrow = "Catálogo infantil",
        DefaultProductHeading = "Para jovens atletas",
        EmptyDescription = "A seleção está sendo atualizada. Fale com a equipe para consultar tamanhos na loja.",
        CardCategory = "kids",
        Category = Category,
        BrandId = BrandId,
        Sort = Sort,
        ContextQuery = new Dictionary<string, string?> { ["modality"] = Modality },
        Catalog = Catalog,
        Filters = Filters
    };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Category = NormalizeSlug(Category);
        Modality = NormalizeSlug(Modality);
        BrandId = BrandId is > 0 ? BrandId : null;
        Sort = CatalogSectionViewModel.NormalizeSort(Sort);
        var products = catalog.GetCatalogAsync(
            new CatalogQuery(null, null, null, BrandId, null, null, true, 1, Sort, Category: Category, Modality: Modality, Audience: PublicCatalogAudience.Kids),
            cancellationToken);
        var filters = catalog.GetFiltersAsync(cancellationToken);
        await Task.WhenAll(products, filters);
        Catalog = await products;
        Filters = await filters ?? new();
    }

    private static string? NormalizeSlug(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant()[..Math.Min(80, value.Trim().Length)];
}
