using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace Morita.LP.Razor.Models;

public sealed class CatalogSectionViewModel
{
    public static IReadOnlyList<CatalogSortOption> SortOptions { get; } =
    [
        new("featured", "Destaques"),
        new("name-asc", "Nome"),
        new("price-asc", "Menor preço"),
        new("price-desc", "Maior preço")
    ];

    public string Route { get; init; } = "/products";
    public string Title { get; init; } = "Produtos";
    public string Eyebrow { get; init; } = "Catálogo Morita";
    public string Description { get; init; } = string.Empty;
    public string CatalogEyebrow { get; init; } = "Estoque online";
    public string DefaultProductHeading { get; init; } = "Produtos";
    public string EmptyDescription { get; init; } = "Tente remover um filtro.";
    public string CardCategory { get; init; } = "products";
    public string? Category { get; init; }
    public int? BrandId { get; init; }
    public string Sort { get; init; } = "featured";
    public IReadOnlyDictionary<string, string?> ContextQuery { get; init; } = new Dictionary<string, string?>();
    public CatalogPage Catalog { get; init; } = new([], 1, CatalogQuery.PageSize, 0, 0, CatalogLoadState.Unavailable);
    public CatalogFilters Filters { get; init; } = new();

    public IReadOnlyList<CatalogFilter> BrandOptions => Filters.PublicBrands;

    public string ResultLabel => Catalog.TotalCount == 1 ? "produto" : "produtos";

    public string ProductHeading => SelectedCategoryLabel is { } category && SelectedBrandLabel is { } brand
        ? $"{category} / {brand}"
        : SelectedCategoryLabel ?? SelectedBrandLabel ?? DefaultProductHeading;

    public string? SelectedCategoryLabel => Filters.Categories
        .FirstOrDefault(filter => string.Equals(filter.Slug, Category, StringComparison.OrdinalIgnoreCase))?.Label;

    public string? SelectedBrandLabel => BrandId.HasValue
        ? BrandOptions.FirstOrDefault(filter => filter.Id == BrandId.Value)?.Label
        : null;

    public bool HasFacetFilters => !string.IsNullOrWhiteSpace(Category) || BrandId.HasValue;

    public string CategoryUrl(string? category) => BuildUrl(category, BrandId, Sort);

    public string BrandUrl(int? brandId) => BuildUrl(Category, brandId, Sort);

    public string ClearFiltersUrl() => BuildUrl(null, null, Sort);

    public IReadOnlyDictionary<string, string> SortFormValues
    {
        get
        {
            var values = new Dictionary<string, string>();
            foreach (var query in ContextQuery)
            {
                if (!string.IsNullOrWhiteSpace(query.Value))
                    values[query.Key] = query.Value!;
            }

            if (!string.IsNullOrWhiteSpace(Category))
                values["category"] = Category;
            if (BrandId.HasValue)
                values["brandId"] = BrandId.Value.ToString(CultureInfo.InvariantCulture);

            return values;
        }
    }

    public static string NormalizeSort(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return SortOptions.Any(option => option.Value == normalized) ? normalized! : "featured";
    }

    private string BuildUrl(string? category, int? brandId, string sort)
    {
        var values = new Dictionary<string, string?>();
        foreach (var query in ContextQuery)
            values[query.Key] = query.Value;

        values["category"] = category;
        values["brandId"] = brandId?.ToString(CultureInfo.InvariantCulture);
        values["sort"] = sort == "featured" ? null : sort;

        return QueryHelpers.AddQueryString(
            Route,
            values.Where(query => !string.IsNullOrWhiteSpace(query.Value))
                .ToDictionary(query => query.Key, query => query.Value));
    }
}

public sealed record CatalogSortOption(string Value, string Label);
