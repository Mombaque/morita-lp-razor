using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public class JiuJitsuModel : PageModel
{
    private readonly CatalogService _catalogService;

    public JiuJitsuModel(CatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public List<Product> Products { get; set; } = new();
    public CatalogLoadState CatalogState { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await _catalogService.GetProductsAsync("jiu-jitsu", cancellationToken);
        CatalogState = result.State;
        Products = result.Products
            .OrderBy(_ => Random.Shared.Next())
            .ToList();
    }
}
