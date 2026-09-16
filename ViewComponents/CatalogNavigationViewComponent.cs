using Microsoft.AspNetCore.Mvc;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.ViewComponents;

public sealed class CatalogNavigationViewComponent(ICatalogClient catalog) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var filters = await catalog.GetFiltersAsync(HttpContext.RequestAborted);
        var currentPath = HttpContext.Request.Path.Value?.TrimEnd('/');
        return View(new CatalogNavigationModel(currentPath, filters?.Categories ?? []));
    }
}

public sealed record CatalogNavigationModel(string? CurrentPath, IReadOnlyList<CatalogFilter> Categories)
{
    public bool IsHome => string.IsNullOrEmpty(CurrentPath);
    public bool IsProducts => CurrentPath == "/products" || CurrentPath?.StartsWith("/products/", StringComparison.Ordinal) == true;
    public bool IsJiuJitsu => CurrentPath == "/jiu-jitsu";
    public bool IsMuayThai => CurrentPath == "/muay-thai";
    public bool IsKids => CurrentPath == "/kids";
    public bool HasCategories => Categories.Count > 0;

    public IReadOnlyList<CatalogNavigationSection> Sections =>
    [
        new("products", "Todos os produtos", "/products", "Categorias de todos os produtos", IsProducts),
        new("jiu-jitsu", "Jiu-Jitsu", "/jiu-jitsu", "Categorias de Jiu-Jitsu", IsJiuJitsu),
        new("muay-thai", "Muay Thai", "/muay-thai", "Categorias de Muay Thai", IsMuayThai),
        new("kids", "Infantil", "/kids", "Categorias infantis", IsKids)
    ];
}

public sealed record CatalogNavigationSection(string Key, string Label, string Href, string MenuLabel, bool IsActive);
