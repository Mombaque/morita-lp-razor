using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public class MuayThaiModel : PageModel
{
    private readonly CatalogService _catalogService;

    public MuayThaiModel(CatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public List<Product> Products { get; set; } = new();
    public CatalogLoadState CatalogState { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await _catalogService.GetProductsAsync("muay-thai", cancellationToken);
        CatalogState = result.State;
        Products = result.Products
            .OrderBy(_ => Random.Shared.Next())
            .ToList();
    }
}
