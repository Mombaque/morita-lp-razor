using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public class ProductsModel(ICatalogClient client) : PageModel
{
    private static readonly string[] SortOptions = ["featured", "relevance", "name-asc", "price-asc", "price-desc"];
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int? CategoryId { get; set; }
    [BindProperty(SupportsGet = true)] public int? ModalityId { get; set; }
    [BindProperty(SupportsGet = true)] public int? BrandId { get; set; }
    [BindProperty(SupportsGet = true)] public int? SizeId { get; set; }
    [BindProperty(SupportsGet = true)] public int? ColorId { get; set; }
    [BindProperty(SupportsGet = true)] public bool? Available { get; set; }
    [BindProperty(SupportsGet = true)] public PublicCatalogAudience? Audience { get; set; }
    [BindProperty(SupportsGet = true)] public decimal? MinimumPrice { get; set; }
    [BindProperty(SupportsGet = true)] public decimal? MaximumPrice { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    public int CurrentPage { get; private set; } = 1;
    public CatalogPage Catalog { get; private set; } = new([], 1, CatalogQuery.PageSize, 0, 0, CatalogLoadState.Unavailable);
    public CatalogFilters Filters { get; private set; } = new();
    public bool HasFilters => !string.IsNullOrWhiteSpace(Search) || CategoryId.HasValue || ModalityId.HasValue || BrandId.HasValue || SizeId.HasValue || ColorId.HasValue || Available.HasValue || Audience is not null || MinimumPrice.HasValue || MaximumPrice.HasValue || Sort != "featured";
    public int ActiveFilterCount => new[]
    {
        !string.IsNullOrWhiteSpace(Search), CategoryId.HasValue, ModalityId.HasValue, BrandId.HasValue,
        SizeId.HasValue, ColorId.HasValue, Available.HasValue, Audience is not null,
        MinimumPrice.HasValue, MaximumPrice.HasValue
    }.Count(value => value);
    public IReadOnlyList<ActiveFilterChip> ActiveFilterChips => BuildActiveFilterChips();
    public string Query(int page) => Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/products", QueryValues(page));
    public string QueryWithout(string key) => Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/products", QueryValues(1, key));

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CurrentPage = 1;
        if (Request.Query.TryGetValue("page", out var pageValue) && int.TryParse(pageValue.ToString(), out var requestedPage) && requestedPage > 1)
            CurrentPage = requestedPage;
        Search = NormalizeSearch(Search);
        MinimumPrice = MinimumPrice is >= 0 ? MinimumPrice : null;
        MaximumPrice = MaximumPrice is >= 0 ? MaximumPrice : null;
        if (MinimumPrice > MaximumPrice) MaximumPrice = null;
        Sort = SortOptions.Contains(Sort?.Trim().ToLowerInvariant()) ? Sort!.Trim().ToLowerInvariant() : "featured";
        if (Sort == "relevance" && string.IsNullOrWhiteSpace(Search)) Sort = "featured";
        var query = new CatalogQuery(Search, CategoryId, ModalityId, BrandId, SizeId, ColorId, Available, CurrentPage, Sort, Audience: Audience, MinimumPrice: MinimumPrice, MaximumPrice: MaximumPrice);
        var products = client.GetCatalogAsync(query, cancellationToken);
        var filters = client.GetFiltersAsync(cancellationToken);
        await Task.WhenAll(products, filters);
        Catalog = await products;
        Filters = await filters ?? new();
    }

    private Dictionary<string, string?> QueryValues(int page, string? without = null) => new Dictionary<string, string?>
    {
        ["search"] = Search, ["categoryId"] = CategoryId?.ToString(), ["modalityId"] = ModalityId?.ToString(), ["brandId"] = BrandId?.ToString(),
        ["sizeId"] = SizeId?.ToString(), ["colorId"] = ColorId?.ToString(), ["available"] = Available?.ToString().ToLowerInvariant(), ["audience"] = Audience?.ToString().ToLowerInvariant(),
        ["minimumPrice"] = MinimumPrice?.ToString(System.Globalization.CultureInfo.InvariantCulture), ["maximumPrice"] = MaximumPrice?.ToString(System.Globalization.CultureInfo.InvariantCulture), ["sort"] = Sort, ["page"] = page.ToString()
    }.Where(x => x.Key != without && !string.IsNullOrWhiteSpace(x.Value)).ToDictionary(x => x.Key, x => x.Value);

    private IReadOnlyList<ActiveFilterChip> BuildActiveFilterChips()
    {
        var chips = new List<ActiveFilterChip>();
        if (!string.IsNullOrWhiteSpace(Search)) chips.Add(new("search", $"Busca: {Search}"));
        if (CategoryId.HasValue) chips.Add(new("categoryId", $"Categoria: {LookupLabel(Filters.Categories, CategoryId, "selecionada")}"));
        if (ModalityId.HasValue) chips.Add(new("modalityId", $"Modalidade: {LookupLabel(Filters.Modalities, ModalityId, "selecionada")}"));
        if (BrandId.HasValue) chips.Add(new("brandId", $"Marca: {LookupLabel(Filters.Brands, BrandId, "selecionada")}"));
        if (SizeId.HasValue) chips.Add(new("sizeId", $"Tamanho: {LookupLabel(Filters.Sizes, SizeId, "selecionado")}"));
        if (ColorId.HasValue) chips.Add(new("colorId", $"Cor: {LookupLabel(Filters.Colors, ColorId, "selecionada")}"));
        if (Available.HasValue) chips.Add(new("available", Available.Value ? "Disponível" : "Indisponível"));
        if (Audience is not null) chips.Add(new("audience", Audience switch
        {
            PublicCatalogAudience.Kids => "Infantil",
            PublicCatalogAudience.All => "Todas as idades",
            _ => "Adulto"
        }));
        if (MinimumPrice.HasValue) chips.Add(new("minimumPrice", $"A partir de {MinimumPrice.Value:0.##}"));
        if (MaximumPrice.HasValue) chips.Add(new("maximumPrice", $"Até {MaximumPrice.Value:0.##}"));
        return chips;
    }

    private static string LookupLabel(IEnumerable<CatalogFilter> filters, int? id, string fallback) =>
        id.HasValue ? filters.FirstOrDefault(filter => filter.Id == id.Value)?.Label ?? fallback : fallback;

    private static string? NormalizeSearch(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(100, value.Trim().Length)];

    public sealed record ActiveFilterChip(string Key, string Label);
}
