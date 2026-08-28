using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public sealed class CatalogClient(HttpClient httpClient, IOptions<CatalogApiOptions> options, ILogger<CatalogClient> logger) : ICatalogClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CatalogApiOptions _options = options.Value;

    public async Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default)
    {
        var path = $"v1/PublicCatalog/products?modality={Uri.EscapeDataString(modality)}";
        var result = await ReadAsync<List<PublicCatalogProductResponse?>>(path, cancellationToken);
        if (!result.IsSuccess || result.Value is null || result.Value.Any(x => x is null)) return CatalogResult.Unavailable();
        var products = result.Value.Select(x => MapLegacy(x!)).Where(x => !string.IsNullOrWhiteSpace(x.Nome)).ToList();
        return products.Count == 0 ? CatalogResult.Empty() : CatalogResult.Success(products);
    }

    public async Task<CatalogPage> GetCatalogAsync(CatalogQuery query, CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>();
        Add("search", query.Search); Add("categoryId", query.CategoryId); Add("modalityId", query.ModalityId); Add("brandId", query.BrandId);
        Add("sizeId", query.SizeId); Add("colorId", query.ColorId); Add("available", query.Available?.ToString().ToLowerInvariant());
        Add("category", query.Category); Add("modality", query.Modality); Add("brand", query.Brand); Add("audience", query.Audience?.ToString().ToLowerInvariant());
        Add("minimumPrice", query.MinimumPrice); Add("maximumPrice", query.MaximumPrice);
        Add("page", Math.Max(1, query.Page)); Add("pageSize", CatalogQuery.PageSize); Add("sort", query.Sort);
        var result = await ReadAsync<PagedResponse>($"v1/storefront/catalog/products?{string.Join('&', parameters)}", cancellationToken);
        if (!result.IsSuccess || result.Value?.Items is null || result.Value.Items.Any(x => x is null)) return UnavailablePage(query);
        var items = result.Value.Items.Select(x => Map(x!)).ToList();
        return new(items, result.Value.Page, result.Value.PageSize, result.Value.TotalCount, result.Value.TotalPages, items.Count == 0 ? CatalogLoadState.Empty : CatalogLoadState.Success);
        void Add(string key, object? value) { if (value is not null && (!value.Equals(0) || key == "page" || key == "pageSize")) parameters.Add($"{key}={Uri.EscapeDataString(value.ToString()!)}"); }
    }

    public async Task<CatalogFilters?> GetFiltersAsync(CancellationToken cancellationToken = default)
    {
        var result = await ReadAsync<CatalogFilters>("v1/storefront/catalog/filters", cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<ProductDetailResult> GetProductAsync(string slug, CancellationToken cancellationToken = default)
    {
        var result = await ReadAsync<ProductResponse>($"v1/storefront/catalog/products/{Uri.EscapeDataString(slug.Trim())}", cancellationToken);
        return result.Status == HttpStatusCode.NotFound ? ProductDetailResult.NotFound() : !result.IsSuccess || result.Value is null ? ProductDetailResult.Unavailable() : ProductDetailResult.Success(Map(result.Value));
    }

    public async Task<CatalogResult> GetRelatedAsync(string slug, int limit = 4, CancellationToken cancellationToken = default)
    {
        var result = await ReadAsync<List<ProductResponse?>>($"v1/storefront/catalog/products/{Uri.EscapeDataString(slug.Trim())}/related?limit={Math.Clamp(limit, 1, 8)}", cancellationToken);
        if (!result.IsSuccess || result.Value is null || result.Value.Any(x => x is null)) return CatalogResult.Unavailable();
        var products = result.Value.Select(x => Map(x!)).ToList();
        return products.Count == 0 ? CatalogResult.Empty() : CatalogResult.Success(products);
    }

    public async Task<CatalogQuoteResult> QuoteAsync(CatalogQuoteRequest request, CancellationToken cancellationToken = default)
    {
        var result = await PostAsync<QuoteResponse>("v1/storefront/catalog/quote", request, cancellationToken);
        if (!result.IsSuccess || result.Value is null || !IsValidQuote(request, result.Value)) return CatalogQuoteResult.Unavailable();
        var lines = result.Value.Lines!;
        foreach (var line in lines)
            line.ImageUrl = NormalizeQuoteImage(line.ImageUrl);
        return CatalogQuoteResult.Success(result.Value.Currency!, result.Value.Total!.Value, lines);
    }

    private static bool IsValidQuote(CatalogQuoteRequest request, QuoteResponse response)
    {
        if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Any(x => x.PublicOfferId == Guid.Empty || x.Quantity is < 1 or > 10) || response.Lines is null || response.Currency is null || string.IsNullOrWhiteSpace(response.Currency) || response.Total is null || response.Total < 0 || response.Lines.Count != request.Lines.Count) return false;
        var expected = request.Lines.ToDictionary(x => x.PublicOfferId);
        if (expected.Count != request.Lines.Count) return false;
        var seen = new HashSet<Guid>();
        decimal sum = 0;
        foreach (var line in response.Lines)
        {
            var status = line.Availability?.ToLowerInvariant();
            if (!seen.Add(line.PublicOfferId) || !expected.TryGetValue(line.PublicOfferId, out var requested) || requested.Quantity != line.Quantity || line.Quantity is < 1 or > 200 || status is not ("available" or "insufficient" or "inactive" or "removed")) return false;
            if (line.ImageUrl is not null && !IsSafeImageUrl(line.ImageUrl)) return false;
            if (status == "available")
            {
                if (string.IsNullOrWhiteSpace(line.Currency) || !string.Equals(line.Currency, response.Currency, StringComparison.OrdinalIgnoreCase) || line.UnitPrice is null or <= 0 || line.LinePrice is null || line.LinePrice != line.UnitPrice * line.Quantity) return false;
                sum += line.LinePrice.Value;
            }
            else if (status == "insufficient" &&
                (string.IsNullOrWhiteSpace(line.Currency) || !string.Equals(line.Currency, response.Currency, StringComparison.OrdinalIgnoreCase) || line.UnitPrice is null or <= 0 || line.LinePrice is null || line.LinePrice != line.UnitPrice * line.Quantity))
                return false;
        }
        return seen.SetEquals(expected.Keys) && sum == response.Total.Value;
    }

    private static bool IsSafeImageUrl(string value) => !value.TrimStart().StartsWith("//", StringComparison.Ordinal) && Uri.TryCreate(value.Trim(), UriKind.RelativeOrAbsolute, out var uri) &&
        (value.TrimStart().StartsWith("/", StringComparison.Ordinal) || uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

    private string? NormalizeQuoteImage(string? image)
    {
        if (string.IsNullOrWhiteSpace(image)) return null;
        image = image.Trim();
        if (image.StartsWith("/", StringComparison.Ordinal))
            return new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), image.TrimStart('/')).ToString();
        return image;
    }

    private async Task<ReadResult<T>> ReadAsync<T>(string path, CancellationToken callerToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 30)));
        try
        {
            using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) { logger.LogWarning("Catalog request returned status {StatusCode}", (int)response.StatusCode); return new(response.StatusCode, false, default); }
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            return new(response.StatusCode, true, await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, timeout.Token));
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested) { logger.LogWarning("Catalog request timed out"); return new(null, false, default); }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "Catalog request unavailable"); return new(null, false, default); }
        catch (JsonException ex) { logger.LogWarning(ex, "Catalog response malformed"); return new(null, false, default); }
    }

    private async Task<ReadResult<T>> PostAsync<T>(string path, object body, CancellationToken callerToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 30)));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: JsonOptions) };
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return new(response.StatusCode, false, default);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            return new(response.StatusCode, true, await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, timeout.Token));
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested) { logger.LogWarning("Catalog quote request timed out"); return new(null, false, default); }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "Catalog quote request unavailable"); return new(null, false, default); }
        catch (JsonException ex) { logger.LogWarning(ex, "Catalog quote response malformed"); return new(null, false, default); }
    }

    private CatalogPage UnavailablePage(CatalogQuery query) => new([], query.Page, CatalogQuery.PageSize, 0, 0, CatalogLoadState.Unavailable);
    private Product Map(ProductResponse p)
    {
        var variants = (p.Variants ?? []).Select(v => { foreach (var offer in v.Offers) { offer.ColorId = v.ColorId; offer.ColorLabel = v.ColorLabel; } v.Images = v.Images.Select(NormalizeImage).OfType<string>().ToList(); return v; }).ToList();
        return new() { Slug = p.Slug?.Trim(), Nome = p.Name?.Trim() ?? "", Alt = p.Name?.Trim() ?? "", Descricao = p.Description?.Trim() ?? "", Details = p.Details ?? [], FabricType = p.FabricType, WeightGsm = p.WeightGsm, Price = p.Price, Currency = p.Currency, Availability = p.Availability ?? "unavailable", Category = p.Category, Modality = p.Modality, Brand = p.Brand, Audience = ParseAudience(p.Audience), FormattedPrice = p.Price is null ? null : $"{p.Currency ?? "R$"} {p.Price:0.00}", Variants = variants, Imagens = variants.SelectMany(v => v.Images).Distinct().ToList() };
    }
    private static PublicCatalogAudience ParseAudience(string? value) => value?.Trim().ToLowerInvariant() switch { "kids" => PublicCatalogAudience.Kids, "all" => PublicCatalogAudience.All, _ => PublicCatalogAudience.Adult };
    private Product MapLegacy(PublicCatalogProductResponse p) => new()
    {
        Nome = p.Name?.Trim() ?? "",
        Alt = p.Name?.Trim() ?? "",
        Descricao = p.Description?.Trim() ?? "",
        FormattedPrice = p.FormattedPrice?.Trim(),
        Imagens = (p.ColorVariants ?? []).Where(v => v is not null).SelectMany(v => v!.Images ?? []).Select(NormalizeLegacyImage).OfType<string>().ToList()
    };
    private string? NormalizeLegacyImage(string? image)
    {
        if (string.IsNullOrWhiteSpace(image)) return null;
        image = image.Trim();
        if (image.StartsWith("//")) return null;
        if (image.StartsWith('/')) return image;
        if (Uri.TryCreate(image, UriKind.Absolute, out var absolute))
            return absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps ? absolute.ToString() : null;
        return new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), image).ToString();
    }
    private string? NormalizeImage(string? image) { if (string.IsNullOrWhiteSpace(image)) return null; image = image.Trim(); if (image.StartsWith("//")) return null; if (image.StartsWith('/')) return new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), image.TrimStart('/')).ToString(); return Uri.TryCreate(image, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri.ToString() : null; }
    private sealed record ReadResult<T>(HttpStatusCode? Status, bool IsSuccess, T? Value);
    private sealed class PagedResponse { public List<ProductResponse?>? Items { get; set; } public int Page { get; set; } public int PageSize { get; set; } public int TotalCount { get; set; } public int TotalPages { get; set; } }
    private sealed class QuoteResponse
    {
        public List<CatalogQuoteLine>? Lines { get; set; }
        public decimal? Total { get; set; }
        public string? Currency { get; set; }
    }
    private sealed class ProductResponse { public string? Slug { get; set; } public string? Name { get; set; } public string? FabricType { get; set; } public int? WeightGsm { get; set; } public string? Description { get; set; } public List<string>? Details { get; set; } public CatalogLookup? Category { get; set; } public CatalogLookup? Modality { get; set; } public CatalogLookup? Brand { get; set; } public string? Audience { get; set; } public decimal? Price { get; set; } public string? Currency { get; set; } public string? Availability { get; set; } public List<ProductVariant>? Variants { get; set; } }
}
