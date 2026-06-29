using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public class JiuJitsuModel : PageModel
{
    private readonly PublicCatalogService _publicCatalogService;

    public JiuJitsuModel(PublicCatalogService publicCatalogService)
    {
        _publicCatalogService = publicCatalogService;
    }

    public List<PublicCatalogProduct> Products { get; set; } = [];
    public string ActiveFilter { get; set; } = "all";
    public bool IsCatalogUnavailable { get; set; }

    public async Task OnGetAsync(string filter = "all")
    {
        ActiveFilter = filter;
        var (category, audience) = GetFilter(filter);

        try
        {
            Products = await _publicCatalogService.GetProductsAsync("Jiu-Jitsu", category, audience);
        }
        catch
        {
            IsCatalogUnavailable = true;
        }
    }

    private static (string? Category, string? Audience) GetFilter(string filter)
    {
        return filter switch
        {
            "adult-kimonos" => ("Kimono", "Adulto"),
            "kids-kimonos" => ("Kimono", "Infantil"),
            "adult-belts" => ("Faixa", "Adulto"),
            "kids-belts" => ("Faixa", "Infantil"),
            _ => (null, null)
        };
    }
}
