using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public interface IOrderClient
{
    Task<OrderResult> GetAsync(string publicOrderNumber, string accessToken, CancellationToken cancellationToken = default);
}

public sealed class OrderClient(HttpClient httpClient, IOptions<CatalogApiOptions> options, IHttpContextAccessor contextAccessor, IHostEnvironment environment, ILogger<OrderClient> logger) : IOrderClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CatalogApiOptions options = options.Value;
    public async Task<OrderResult> GetAsync(string publicOrderNumber, string accessToken, CancellationToken cancellationToken = default)
    {
        if (!OrderAccessCookieStore.IsValidOrderNumber(publicOrderNumber) || accessToken.Length is < 32 or > 200) return OrderResult.Failure(OrderLoadState.Malformed);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 30)));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"v1/storefront/orders/{Uri.EscapeDataString(publicOrderNumber)}");
            request.Headers.TryAddWithoutValidation("X-Order-Access-Token", accessToken);
            if (contextAccessor.HttpContext is { } context) request.Headers.TryAddWithoutValidation("X-Morita-Client-IP", ClientIdentityResolver.Resolve(context, environment));
            if (!string.IsNullOrWhiteSpace(options.ProxySecret)) request.Headers.TryAddWithoutValidation("X-Morita-Proxy-Secret", options.ProxySecret);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return OrderResult.Failure(OrderLoadState.Unauthorized);
            if ((int)response.StatusCode >= 500) return OrderResult.Failure(OrderLoadState.Unavailable, "O pedido não pôde ser consultado agora.");
            if (!response.IsSuccessStatusCode) return OrderResult.Failure(OrderLoadState.Unavailable);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var dto = await JsonSerializer.DeserializeAsync<OrderDto>(stream, JsonOptions, timeout.Token);
            return dto is not null && TryMap(dto, publicOrderNumber, out var order) ? new(OrderLoadState.Success, order) : OrderResult.Failure(OrderLoadState.Malformed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { logger.LogWarning("Order API request timed out"); return OrderResult.Failure(OrderLoadState.Timeout, "A consulta demorou. Tente novamente."); }
        catch (HttpRequestException exception) { logger.LogWarning(exception, "Order API unavailable"); return OrderResult.Failure(OrderLoadState.Unavailable, "O pedido não pôde ser consultado agora."); }
        catch (JsonException exception) { logger.LogWarning(exception, "Order API response malformed"); return OrderResult.Failure(OrderLoadState.Malformed); }
    }

    private static bool TryMap(OrderDto x, string requested, out PublicOrder? order)
    {
        order = null;
        var address = ParseAddress(x.PickupAddressJson);
        if (!string.Equals(x.PublicOrderNumber, requested, StringComparison.OrdinalIgnoreCase) || !OrderAccessCookieStore.IsValidOrderNumber(x.PublicOrderNumber ?? "") || !Status(x.PaymentStatus, "pending", "processing", "approved", "conversionpending", "converted", "failed", "cancelled", "expired", "refundpending", "refunded") || !Status(x.FulfillmentStatus, "pending", "preparingpickup", "readyforpickup", "pickedup") || x.Amount < 0 || x.Amount != decimal.Round(x.Amount, 2) || x.Amount > 100000000 || !Currency(x.Currency) || x.CreatedAt == default || x.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(5) || !Text(x.PublicOrderNumber, 19) || !Text(x.PaymentStatus, 30) || !Text(x.FulfillmentStatus, 30) || string.IsNullOrWhiteSpace(x.PickupDisplayName) || x.PickupDisplayName.Length > 160 || address is null || string.IsNullOrWhiteSpace(x.PickupHours) || x.PickupHours.Length > 1000 || x.PickupInstructions?.Length > 2000 || x.Lines is null || x.Lines.Count == 0 || x.Lines.Any(l => l is null || !Text(l.Description, 300) || !Text(l.Presentation, 300) || l.Quantity is < 1 or > 100 || l.UnitPrice < 0 || l.Total < 0 || l.Total != decimal.Round(l.Total, 2) || l.Total != decimal.Round(l.UnitPrice * l.Quantity, 2)) || x.Lines.Sum(l => l.Total) != x.Amount) return false;
        order = new() { PublicOrderNumber = x.PublicOrderNumber!.Trim().ToUpperInvariant(), PaymentStatus = x.PaymentStatus!.Trim().ToLowerInvariant(), FulfillmentStatus = x.FulfillmentStatus!.Trim().ToLowerInvariant(), Amount = x.Amount, Currency = x.Currency!.Trim().ToUpperInvariant(), CreatedAt = x.CreatedAt, FulfillmentUpdatedAt = x.FulfillmentUpdatedAt, PickupDisplayName = x.PickupDisplayName.Trim(), PickupAddress = address, PickupHours = x.PickupHours.Trim(), PickupInstructions = x.PickupInstructions?.Trim() ?? "", Lines = x.Lines.Select(l => new PublicOrderLine { Description = l.Description!.Trim(), Presentation = l.Presentation!.Trim(), Quantity = l.Quantity, UnitPrice = l.UnitPrice, Total = l.Total }).ToList() };
        return true;
    }
    private static bool Status(string? value, params string[] allowed) => value is not null && allowed.Contains(value.Trim().ToLowerInvariant());
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;
    private static bool Currency(string? value) => value is not null && value.Trim().Length == 3 && value.Trim().All(char.IsAsciiLetter);
    private static CheckoutAddress? ParseAddress(JsonElement value)
    {
        try
        {
            using var document = value.ValueKind == JsonValueKind.String ? JsonDocument.Parse(value.GetString() ?? "") : null;
            if (document is not null) value = document.RootElement.Clone();
            if (value.ValueKind != JsonValueKind.Object) return null;
            string? Get(string name) => value.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            var street = Get("street"); var number = Get("number"); var neighborhood = Get("neighborhood"); var city = Get("city"); var state = Get("state"); var postal = Get("postalCode");
            return string.IsNullOrWhiteSpace(street) || street.Length > 160 || string.IsNullOrWhiteSpace(number) || number.Length > 40 || string.IsNullOrWhiteSpace(neighborhood) || neighborhood.Length > 120 || string.IsNullOrWhiteSpace(city) || city.Length > 120 || string.IsNullOrWhiteSpace(state) || state.Length > 40 || string.IsNullOrWhiteSpace(postal) || postal.Length > 30 || Get("complement")?.Length > 160 ? null : new CheckoutAddress { Street = street.Trim(), Number = number.Trim(), Complement = Get("complement")?.Trim(), Neighborhood = neighborhood.Trim(), City = city.Trim(), State = state.Trim(), PostalCode = postal.Trim() };
        } catch (JsonException) { return null; }
    }
    private sealed class OrderDto { public string? PublicOrderNumber { get; set; } public string? PaymentStatus { get; set; } public string? FulfillmentStatus { get; set; } public decimal Amount { get; set; } public string? Currency { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset? FulfillmentUpdatedAt { get; set; } public string? PickupDisplayName { get; set; } public JsonElement PickupAddressJson { get; set; } public string? PickupHours { get; set; } public string? PickupInstructions { get; set; } public List<LineDto>? Lines { get; set; } }
    private sealed class AddressDto { public string? Street { get; set; } public string? Number { get; set; } public string? Complement { get; set; } public string? Neighborhood { get; set; } public string? City { get; set; } public string? State { get; set; } public string? PostalCode { get; set; } }
    private sealed class LineDto { public string? Description { get; set; } public string? Presentation { get; set; } public int Quantity { get; set; } public decimal UnitPrice { get; set; } public decimal Total { get; set; } }
}
