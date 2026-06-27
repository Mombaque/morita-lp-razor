using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public class JiuJitsuModel : PageModel
{
    private readonly ProductService _productService;

    public JiuJitsuModel(ProductService productService)
    {
        _productService = productService;
    }

    public List<Product> Products { get; set; } = new();

    public void OnGet()
    {
        Products = _productService.GetJiuJitsuProducts()
            .OrderBy(_ => Random.Shared.Next())
            .ToList();
    }
}
