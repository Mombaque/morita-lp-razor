using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public class ProdutoModel : PageModel
{
    private readonly PublicCatalogService _publicCatalogService;

    public ProdutoModel(PublicCatalogService publicCatalogService)
    {
        _publicCatalogService = publicCatalogService;
    }

    public PublicCatalogProduct? Product { get; set; }
    public List<PublicCatalogProduct> RelatedProducts { get; set; } = [];
    public bool IsCatalogUnavailable { get; set; }
    public bool IsNotFound { get; set; }

    public async Task OnGetAsync(string? slug = null)
    {
        slug = string.IsNullOrWhiteSpace(slug) ? Request.Query["slug"].ToString() : slug;

        if (string.IsNullOrWhiteSpace(slug))
        {
            IsNotFound = true;
            return;
        }

        try
        {
            Product = await _publicCatalogService.GetProductBySlugAsync(slug);
            if (Product == null)
            {
                IsNotFound = true;
                return;
            }

            try
            {
                RelatedProducts = await _publicCatalogService.GetRelatedProductsAsync(slug);
            }
            catch
            {
                RelatedProducts = [];
            }
        }
        catch
        {
            IsCatalogUnavailable = true;
        }
    }
}
