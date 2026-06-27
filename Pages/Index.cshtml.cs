using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public class IndexModel : PageModel
{
    private readonly ProductService _productService;

    public IndexModel(ProductService productService)
    {
        _productService = productService;
    }

    public List<CarouselSlide> Slides { get; set; } = new();

    public void OnGet()
    {
        var fixedFirstImage = "/images/kimono/adulto/itg-azul.jpg";

        var jiuJitsuImages = _productService.GetJiuJitsuProducts()
            .SelectMany(p => p.Imagens)
            .Where(img => img != fixedFirstImage)
            .Select(img => new CarouselSlide { Image = img, CategoryName = "Jiu-Jitsu", CategoryUrl = "/JiuJitsu" })
            .OrderBy(_ => Random.Shared.Next())
            .Take(4)
            .ToList();

        var muayThaiImages = _productService.GetMuayThaiProducts()
            .SelectMany(p => p.Imagens)
            .Where(img => img != fixedFirstImage)
            .Select(img => new CarouselSlide { Image = img, CategoryName = "Muay Thai", CategoryUrl = "/MuayThai" })
            .OrderBy(_ => Random.Shared.Next())
            .Take(4)
            .ToList();

        var randomSlides = jiuJitsuImages.Concat(muayThaiImages)
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        Slides.Add(new CarouselSlide { Image = fixedFirstImage, CategoryName = "Jiu-Jitsu", CategoryUrl = "/JiuJitsu" });
        Slides.AddRange(randomSlides);
    }
}

public class CarouselSlide
{
    public string Image { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryUrl { get; set; } = string.Empty;
}
