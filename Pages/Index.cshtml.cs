using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public class IndexModel(ICatalogClient catalog) : PageModel
{
    public CatalogPage Catalog { get; private set; } = new([], 1, CatalogQuery.PageSize, 0, 0, CatalogLoadState.Unavailable);
    public IReadOnlyList<Product> FeaturedProducts => Catalog.Items.Take(8).ToList();
    public Product? HeroProduct => FeaturedProducts.FirstOrDefault(product => product.Imagens.Count > 0);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Catalog = await catalog.GetCatalogAsync(
            new CatalogQuery(null, null, null, null, null, null, true, 1),
            cancellationToken);
    }
}
