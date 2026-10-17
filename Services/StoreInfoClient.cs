using System.Text.Json;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public interface IStoreInfoClient
{
    Task<StorefrontStore?> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class StoreInfoClient(
    HttpClient httpClient,
    IOptions<CatalogApiOptions> options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<StoreInfoClient> logger,
    CatalogResponseCache? cache = null) : IStoreInfoClient
{
    private const string Path = "v1/storefront/store";
    private static readonly object RequestItemKey = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CatalogApiOptions _options = options.Value;

    public async Task<StorefrontStore?> GetAsync(CancellationToken cancellationToken = default)
    {
        var items = httpContextAccessor.HttpContext?.Items;
        if (items is not null && items.TryGetValue(RequestItemKey, out var memoized))
            return memoized as StorefrontStore;
        var store = await ReadAsync(cancellationToken);
        if (items is not null)
            items[RequestItemKey] = store;
        return store;
    }

    private async Task<StorefrontStore?> ReadAsync(CancellationToken callerToken)
    {
        if (cache?.TryGet(Path, out var cached) == true)
            return Deserialize(cached);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 5)));
        try
        {
            using var response = await httpClient.GetAsync(Path, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Store info request returned status {StatusCode}", (int)response.StatusCode);
                return null;
            }
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            var store = Deserialize(body);
            if (store is not null)
                cache?.Set(Path, body);
            return store;
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested) { logger.LogWarning("Store info request timed out"); return null; }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "Store info request unavailable"); return null; }
        catch (JsonException ex) { logger.LogWarning(ex, "Store info response malformed"); return null; }
    }

    private static StorefrontStore? Deserialize(byte[] body) => JsonSerializer.Deserialize<StorefrontStore>(body, JsonOptions);
}
