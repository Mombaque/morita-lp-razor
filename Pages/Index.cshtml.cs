using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public class IndexModel : PageModel
{
    private readonly CatalogService _catalogService;

    public IndexModel(CatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public List<CarouselSlide> Slides { get; set; } = new();
    public CatalogLoadState CatalogState { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var fixedFirstImage = "/images/kimono/adulto/itg-azul.jpg";

        var jiuJitsu = await _catalogService.GetProductsAsync("jiu-jitsu", cancellationToken);
        var muayThai = await _catalogService.GetProductsAsync("muay-thai", cancellationToken);
        var hasProducts = jiuJitsu.State == CatalogLoadState.Success || muayThai.State == CatalogLoadState.Success;
        var hasUnavailableCatalog = jiuJitsu.State == CatalogLoadState.Unavailable || muayThai.State == CatalogLoadState.Unavailable;
        CatalogState = hasProducts && hasUnavailableCatalog
            ? CatalogLoadState.Partial
            : hasProducts
                ? CatalogLoadState.Success
                : hasUnavailableCatalog
                    ? CatalogLoadState.Unavailable
                    : CatalogLoadState.Empty;
        var jiuJitsuImages = jiuJitsu.Products
            .SelectMany(p => p.Imagens)
            .Where(img => img != fixedFirstImage)
            .Select(img => new CarouselSlide { Image = img, CategoryName = "Jiu-Jitsu", CategoryUrl = "/jiu-jitsu" })
            .OrderBy(_ => Random.Shared.Next())
            .Take(4)
            .ToList();

        var muayThaiImages = muayThai.Products
            .SelectMany(p => p.Imagens)
            .Where(img => img != fixedFirstImage)
            .Select(img => new CarouselSlide { Image = img, CategoryName = "Muay Thai", CategoryUrl = "/muay-thai" })
            .OrderBy(_ => Random.Shared.Next())
            .Take(4)
            .ToList();

        var randomSlides = jiuJitsuImages.Concat(muayThaiImages)
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        if (jiuJitsu.State == CatalogLoadState.Success || muayThai.State == CatalogLoadState.Success)
        {
            Slides.Add(new CarouselSlide { Image = fixedFirstImage, CategoryName = "Jiu-Jitsu", CategoryUrl = "/jiu-jitsu" });
            Slides.AddRange(randomSlides);
        }
    }
}

public class CarouselSlide
{
    public string Image { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryUrl { get; set; } = string.Empty;
}
