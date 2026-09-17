using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public interface IPublicDeliveryClient
{
    Task<PublicDelivery?> GetAsync(string publicToken, CancellationToken cancellationToken = default);
}

public sealed class PublicDeliveryClient(IHttpClientFactory httpClientFactory, IOptions<DeliveryTrackingOptions> options, IHttpContextAccessor httpContextAccessor) : IPublicDeliveryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DeliveryTrackingOptions _options = options.Value;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    public async Task<PublicDelivery?> GetAsync(string publicToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicToken)) return null;
        var path = _options.PublicDeliveryPath.Replace("{publicToken}", Uri.EscapeDataString(publicToken), StringComparison.Ordinal);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        var flyClientIp = _httpContextAccessor.HttpContext?.Request.Headers["Fly-Client-IP"].ToString();
        if (IPAddress.TryParse(flyClientIp, out var parsedClientIp) && !string.IsNullOrWhiteSpace(_options.ProxySecret))
        {
            request.Headers.TryAddWithoutValidation("X-Morita-Client-IP", parsedClientIp.ToString());
            request.Headers.TryAddWithoutValidation("X-Morita-Proxy-Secret", _options.ProxySecret);
        }
        var client = httpClientFactory.CreateClient("public-delivery");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PublicDelivery>(JsonOptions, cancellationToken);
    }
}
