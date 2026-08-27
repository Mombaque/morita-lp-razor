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
        var method = x.FulfillmentMethod?.Trim().ToLowerInvariant();
        var pickupAddress = method == "pickup" ? ParseAddress(x.PickupAddressJson) : null;
        var validPickup = method == "pickup" && !string.IsNullOrWhiteSpace(x.PickupDisplayName) && x.PickupDisplayName.Length <= 160 && pickupAddress is not null && !string.IsNullOrWhiteSpace(x.PickupHours) && x.PickupHours.Length <= 1000 && x.PickupInstructions?.Length is null or <= 2000;
        var validShipping = method == "shipping" && ValidShipping(x.Shipping);
        if (!string.Equals(x.PublicOrderNumber, requested, StringComparison.OrdinalIgnoreCase) || !OrderAccessCookieStore.IsValidOrderNumber(x.PublicOrderNumber ?? "") || !Status(x.PaymentStatus, "pending", "processing", "approved", "conversionpending", "cancellationpending", "converted", "failed", "cancelled", "expired", "refundpending", "refunded") || !Status(x.FulfillmentStatus, "pending", "preparingpickup", "readyforpickup", "pickedup", "cancelled") || !validPickup && !validShipping || x.Amount < 0 || x.Amount != decimal.Round(x.Amount, 2) || x.Amount > 100000000 || !Currency(x.Currency) || x.CreatedAt == default || x.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(5) || !Text(x.PublicOrderNumber, 19) || !Text(x.PaymentStatus, 30) || !Text(x.FulfillmentStatus, 30) || x.Lines is null || x.Lines.Count == 0 || x.Lines.Any(l => l is null || !Text(l.Description, 300) || !Text(l.Presentation, 300) || l.Quantity is < 1 or > 100 || l.UnitPrice < 0 || l.Total < 0 || l.Total != decimal.Round(l.Total, 2) || l.Total != decimal.Round(l.UnitPrice * l.Quantity, 2)) || !ValidShipment(x.Shipment)) return false;
        var expectedAmount = x.Lines.Sum(line => line.Total) + (x.Shipping?.Price ?? 0);
        if (expectedAmount != x.Amount) return false;
        order = new() { PublicOrderNumber = x.PublicOrderNumber!.Trim().ToUpperInvariant(), PaymentStatus = x.PaymentStatus!.Trim().ToLowerInvariant(), FulfillmentStatus = x.FulfillmentStatus!.Trim().ToLowerInvariant(), FulfillmentMethod = method!, Amount = x.Amount, Currency = x.Currency!.Trim().ToUpperInvariant(), CreatedAt = x.CreatedAt, FulfillmentUpdatedAt = x.FulfillmentUpdatedAt, PickupDisplayName = x.PickupDisplayName?.Trim() ?? "", PickupAddress = pickupAddress, PickupHours = x.PickupHours?.Trim() ?? "", PickupInstructions = x.PickupInstructions?.Trim() ?? "", Shipping = x.Shipping is null ? null : Map(x.Shipping), Shipment = x.Shipment is null ? null : Map(x.Shipment), Lines = x.Lines.Select(l => new PublicOrderLine { Description = l.Description!.Trim(), Presentation = l.Presentation!.Trim(), Quantity = l.Quantity, UnitPrice = l.UnitPrice, Total = l.Total }).ToList() };
        return true;
    }
    private static bool Status(string? value, params string[] allowed) => value is not null && allowed.Contains(value.Trim().ToLowerInvariant());
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;
    private static bool Currency(string? value) => value is not null && value.Trim().Length == 3 && value.Trim().All(char.IsAsciiLetter);
    private static bool ValidShipping(ShippingDto? x) => x is not null && Text(x.CarrierName, 120) && Text(x.ServiceName, 120) && x.Price >= 0 && x.Price == decimal.Round(x.Price, 2) && x.MinimumDeliveryDays >= 0 && x.MaximumDeliveryDays >= x.MinimumDeliveryDays && ValidAddress(x.Address);
    private static bool ValidAddress(AddressDto? x) => x is not null && Text(x.Recipient, 120) && Text(x.Street, 160) && Text(x.Number, 30) && x.Complement?.Length is null or <= 120 && Text(x.Neighborhood, 120) && Text(x.City, 120) && x.State?.Trim().Length == 2 && Text(x.PostalCode, 30) && string.Equals(x.CountryCode?.Trim(), "BR", StringComparison.OrdinalIgnoreCase);
    private static bool ValidShipment(ShipmentDto? x) => x is null || Status(x.Status, "awaitinglabel", "labelpurchasepending", "labelpurchased", "intransit", "delivered", "cancellationpending", "cancelled", "exception") && Text(x.CarrierName, 120) && Text(x.ServiceName, 120) && x.TrackingCode?.Length is null or <= 160 && x.UpdatedAt != default;
    private static ShippingSnapshot Map(ShippingDto x) => new() { CarrierName = x.CarrierName!.Trim(), ServiceName = x.ServiceName!.Trim(), Price = x.Price, MinimumDeliveryDays = x.MinimumDeliveryDays, MaximumDeliveryDays = x.MaximumDeliveryDays, Address = Map(x.Address!) };
    private static CheckoutAddress Map(AddressDto x) => new() { Recipient = x.Recipient!.Trim(), Street = x.Street!.Trim(), Number = x.Number!.Trim(), Complement = x.Complement?.Trim(), Neighborhood = x.Neighborhood!.Trim(), City = x.City!.Trim(), State = x.State!.Trim().ToUpperInvariant(), PostalCode = x.PostalCode!.Trim(), CountryCode = x.CountryCode!.Trim().ToUpperInvariant() };
    private static PublicShipment Map(ShipmentDto x) => new() { Status = x.Status!.Trim().ToLowerInvariant(), CarrierName = x.CarrierName!.Trim(), ServiceName = x.ServiceName!.Trim(), TrackingCode = x.TrackingCode?.Trim(), UpdatedAt = x.UpdatedAt, ShippedAt = x.ShippedAt, DeliveredAt = x.DeliveredAt };
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
    private sealed class OrderDto { public string? PublicOrderNumber { get; set; } public string? PaymentStatus { get; set; } public string? FulfillmentStatus { get; set; } public decimal Amount { get; set; } public string? Currency { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset? FulfillmentUpdatedAt { get; set; } public string? PickupDisplayName { get; set; } public JsonElement PickupAddressJson { get; set; } public string? PickupHours { get; set; } public string? PickupInstructions { get; set; } public string? FulfillmentMethod { get; set; } public ShippingDto? Shipping { get; set; } public ShipmentDto? Shipment { get; set; } public List<LineDto>? Lines { get; set; } }
    private sealed class AddressDto { public string? Recipient { get; set; } public string? Street { get; set; } public string? Number { get; set; } public string? Complement { get; set; } public string? Neighborhood { get; set; } public string? City { get; set; } public string? State { get; set; } public string? PostalCode { get; set; } public string? CountryCode { get; set; } }
    private sealed class ShippingDto { public string? CarrierName { get; set; } public string? ServiceName { get; set; } public decimal Price { get; set; } public int MinimumDeliveryDays { get; set; } public int MaximumDeliveryDays { get; set; } public AddressDto? Address { get; set; } }
    private sealed class ShipmentDto { public string? Status { get; set; } public string? CarrierName { get; set; } public string? ServiceName { get; set; } public string? TrackingCode { get; set; } public DateTimeOffset UpdatedAt { get; set; } public DateTimeOffset? ShippedAt { get; set; } public DateTimeOffset? DeliveredAt { get; set; } }
    private sealed class LineDto { public string? Description { get; set; } public string? Presentation { get; set; } public int Quantity { get; set; } public decimal UnitPrice { get; set; } public decimal Total { get; set; } }
}
