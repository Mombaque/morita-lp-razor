using System.Net;
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 30)));
        var path = $"v1/PublicCatalog/products?modality={Uri.EscapeDataString(modality)}";

        try
        {
            using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogWarning("Catalog request returned not found for modality {Modality}", modality);
                return CatalogResult.Unavailable();
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Catalog request returned status {StatusCode} for modality {Modality}", (int)response.StatusCode, modality);
                return CatalogResult.Unavailable();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var products = await JsonSerializer.DeserializeAsync<List<PublicCatalogProductResponse?>>(stream, JsonOptions, timeout.Token);
            if (products is null)
                return CatalogResult.Unavailable();
            if (products.Any(product => product is null))
                return CatalogResult.Unavailable();

            var mapped = products.Select(product => Map(product!)).Where(product => !string.IsNullOrWhiteSpace(product.Nome)).ToList();
            return mapped.Count == 0 ? CatalogResult.Empty() : CatalogResult.Success(mapped);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Catalog request timed out for modality {Modality}", modality);
            return CatalogResult.Unavailable();
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Catalog request unavailable for modality {Modality}", modality);
            return CatalogResult.Unavailable();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Catalog response malformed for modality {Modality}", modality);
            return CatalogResult.Unavailable();
        }
    }

    private static Product Map(PublicCatalogProductResponse product) => new()
    {
        Nome = product.Name?.Trim() ?? string.Empty,
        Alt = product.Name?.Trim() ?? string.Empty,
        Descricao = product.Description?.Trim() ?? string.Empty,
        FormattedPrice = string.IsNullOrWhiteSpace(product.FormattedPrice) ? null : product.FormattedPrice.Trim(),
        Imagens = (product.ColorVariants ?? [])
            .Where(variant => variant is not null)
            .SelectMany(variant => variant!.Images ?? [])
            .Where(image => !string.IsNullOrWhiteSpace(image))
            .Select(image => image!.Trim())
            .ToList()
    };
}
