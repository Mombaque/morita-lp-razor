using System.Net;
using System.Text.Json;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public class PublicCatalogService(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<List<PublicCatalogProduct>> GetProductsAsync(string modality, string? category = null, string? audience = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["modality"] = modality,
            ["category"] = category,
            ["audience"] = audience
        };

        return GetJsonAsync<List<PublicCatalogProduct>>($"/v1/PublicCatalog/products{BuildQuery(query)}");
    }

    public async Task<PublicCatalogProduct?> GetProductBySlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        try
        {
            return await GetJsonAsync<PublicCatalogProduct>($"/v1/PublicCatalog/products/{Uri.EscapeDataString(slug)}");
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task<List<PublicCatalogProduct>> GetRelatedProductsAsync(string slug, int limit = 4)
    {
        return GetJsonAsync<List<PublicCatalogProduct>>($"/v1/PublicCatalog/products/{Uri.EscapeDataString(slug)}/related?limit={limit}");
    }

    private async Task<T> GetJsonAsync<T>(string path)
    {
        using var response = await httpClient.GetAsync(path);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Public catalog request failed with {(int)response.StatusCode}.", null, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        return result ?? throw new InvalidOperationException("Public catalog returned an empty response.");
    }

    private static string BuildQuery(Dictionary<string, string?> values)
    {
        var parts = values
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}")
            .ToList();

        return parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts)}";
    }
}
