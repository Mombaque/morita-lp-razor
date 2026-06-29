using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public class KarateModel : PageModel
{
    private readonly PublicCatalogService _publicCatalogService;

    public KarateModel(PublicCatalogService publicCatalogService)
    {
        _publicCatalogService = publicCatalogService;
    }

    public List<PublicCatalogProduct> Products { get; set; } = [];
    public bool IsCatalogUnavailable { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            Products = await _publicCatalogService.GetProductsAsync("Karatê");
        }
        catch
        {
            IsCatalogUnavailable = true;
        }
    }
}
