using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;

namespace Morita.LP.Razor.Services;

// Short-lived cache of successful public catalog GET bodies, so page views do not each pay an API round trip.
// Raw bytes are cached (not mapped models) because mapping mutates the deserialized responses.
// Entries are keyed by storefront host: the API resolves the store (tenant) from it.
// Quotes are never cached; cart and checkout always see live price and stock.
public sealed class CatalogResponseCache(IHttpContextAccessor httpContextAccessor, IOptions<CatalogApiOptions> options) : IDisposable
{
    private const long MaximumBytes = 32 * 1024 * 1024;
    private const int MaximumEntryBytes = 1024 * 1024;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaximumBytes });
    private readonly TimeSpan _lifetime = TimeSpan.FromSeconds(Math.Clamp(options.Value.CacheSeconds, 0, 300));

    public bool Enabled => _lifetime > TimeSpan.Zero;

    public bool TryGet(string path, out byte[] body)
    {
        if (Enabled && _cache.TryGetValue(Key(path), out byte[]? cached) && cached is not null)
        {
            body = cached;
            return true;
        }
        body = [];
        return false;
    }

    public void Set(string path, byte[] body)
    {
        if (!Enabled || body.Length is 0 or > MaximumEntryBytes)
            return;
        _cache.Set(Key(path), body, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _lifetime, Size = body.Length });
    }

    private string Key(string path)
    {
        var host = httpContextAccessor.HttpContext is { } context ? StorefrontApiHeadersHandler.NormalizeHost(context.Request.Host) : null;
        return $"{host}\n{path}";
    }

    public void Dispose() => _cache.Dispose();
}
