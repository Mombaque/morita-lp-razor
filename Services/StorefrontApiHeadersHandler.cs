using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;

namespace Morita.LP.Razor.Services;

// The API resolves the store (tenant) from X-Morita-Storefront-Host and trusts it, like the client IP,
// only together with the proxy secret. Every server-to-API call must carry all three.
public sealed class StorefrontApiHeadersHandler(
    IHttpContextAccessor httpContextAccessor,
    IOptions<CatalogApiOptions> options,
    IHostEnvironment environment) : DelegatingHandler
{
    public const string ClientIpHeader = "X-Morita-Client-IP";
    public const string ProxySecretHeader = "X-Morita-Proxy-Secret";
    public const string StorefrontHostHeader = "X-Morita-Storefront-Host";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var proxySecret = options.Value.ProxySecret;
        var hasProxySecret = !string.IsNullOrWhiteSpace(proxySecret);
        if (httpContextAccessor.HttpContext is { } context)
        {
            SetHeader(request, ClientIpHeader, ClientIdentityResolver.Resolve(context, environment));
            var storefrontHost = NormalizeHost(context.Request.Host);
            if (hasProxySecret && storefrontHost is not null)
                SetHeader(request, StorefrontHostHeader, storefrontHost);
        }

        if (hasProxySecret)
            SetHeader(request, ProxySecretHeader, proxySecret!);

        return base.SendAsync(request, cancellationToken);
    }

    public static string? NormalizeHost(HostString host)
    {
        if (!host.HasValue)
            return null;

        var normalized = host.Host.Trim().TrimEnd('.').ToLowerInvariant();
        return normalized.Length is 0 or > 253 ? null : normalized;
    }

    private static void SetHeader(HttpRequestMessage request, string name, string value)
    {
        request.Headers.Remove(name);
        request.Headers.TryAddWithoutValidation(name, value);
    }
}
