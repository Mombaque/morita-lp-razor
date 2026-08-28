using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Models;

public enum CheckoutLoadState { Success, Validation, Conflict, NotFound, RateLimited, Unavailable, Timeout, Malformed }

public sealed record CheckoutResult(CheckoutLoadState State, CheckoutResponse? Checkout, string? Message = null)
{
    public static CheckoutResult Failure(CheckoutLoadState state, string? message = null) => new(state, null, message);
}

public enum PaymentLoadState { Success, Validation, NotFound, RateLimited, Unavailable, Timeout, Malformed }
public sealed record PaymentResult(PaymentLoadState State, PixPayment? Payment, string? Message = null)
{
    public static PaymentResult Failure(PaymentLoadState state, string? message = null) => new(state, null, message);
}

public sealed class PixPayment
{
    public string Status { get; init; } = "";
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
    public string PixCopyPaste { get; init; } = "";
    public string QrCodePngDataUri { get; init; } = "";
    public string? PublicOrderNumber { get; init; }
}

public enum OrderLoadState { Success, NotFound, Unauthorized, Unavailable, Timeout, Malformed }
public sealed record OrderResult(OrderLoadState State, PublicOrder? Order, string? Message = null)
{
    public static OrderResult Failure(OrderLoadState state, string? message = null) => new(state, null, message);
}

public sealed class PublicOrder
{
    public string PublicOrderNumber { get; init; } = "";
    public string PaymentStatus { get; init; } = "";
    public string FulfillmentStatus { get; init; } = "";
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? FulfillmentUpdatedAt { get; init; }
    public string FulfillmentMethod { get; init; } = "pickup";
    public string PickupDisplayName { get; init; } = "";
    public CheckoutAddress? PickupAddress { get; init; }
    public string PickupHours { get; init; } = "";
    public string PickupInstructions { get; init; } = "";
    public ShippingSnapshot? Shipping { get; init; }
    public PublicShipment? Shipment { get; init; }
    public IReadOnlyList<PublicOrderLine> Lines { get; init; } = [];
}

public sealed class PublicOrderLine
{
    public string Description { get; init; } = "";
    public string Presentation { get; init; } = "";
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Total { get; init; }
}

public sealed record CheckoutConfigurationResult(CheckoutLoadState State, CheckoutConfiguration? Configuration)
{
    public static CheckoutConfigurationResult Failure(CheckoutLoadState state) => new(state, null);
}

public sealed class CheckoutConfiguration
{
    public bool PickupEnabled { get; init; }
    public bool ShippingEnabled { get; init; }
    public Guid? PublicPickupId { get; init; }
    public string Currency { get; init; } = "BRL";
    public PickupSnapshot? Pickup { get; init; }
}

public sealed class CheckoutResponse
{
    public Guid PublicCheckoutId { get; init; }
    public string Status { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset AccessExpiresAt { get; init; }
    public IReadOnlyList<CheckoutLine> Lines { get; init; } = [];
    public decimal MerchandiseTotal { get; init; }
    public decimal DiscountTotal { get; init; }
    public decimal FreightTotal { get; init; }
    public decimal Total { get; init; }
    public string Currency { get; init; } = "";
    public string FulfillmentMethod { get; init; } = "pickup";
    public PickupSnapshot? Pickup { get; init; }
    public ShippingSnapshot? Shipping { get; init; }
    public CheckoutContact Contact { get; init; } = new();
}

public sealed class CheckoutLine
{
    public Guid PublicOfferId { get; init; }
    public int Quantity { get; init; }
    public string Presentation { get; init; } = "";
    public string? ImageUrl { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }
}

public sealed class PickupSnapshot
{
    public Guid PublicPickupId { get; init; }
    public string DisplayName { get; init; } = "";
    public CheckoutAddress Address { get; init; } = new();
    public string Hours { get; init; } = "";
    public string Instructions { get; init; } = "";
}

public sealed class CheckoutAddress
{
    public string Recipient { get; init; } = "";
    public string Street { get; init; } = "";
    public string Number { get; init; } = "";
    public string? Complement { get; init; }
    public string Neighborhood { get; init; } = "";
    public string City { get; init; } = "";
    public string State { get; init; } = "";
    public string PostalCode { get; init; } = "";
    public string CountryCode { get; init; } = "BR";
}

public sealed class ShippingSnapshot
{
    public string CarrierName { get; init; } = "";
    public string ServiceName { get; init; } = "";
    public decimal Price { get; init; }
    public int MinimumDeliveryDays { get; init; }
    public int MaximumDeliveryDays { get; init; }
    public CheckoutAddress Address { get; init; } = new();
}

public sealed class PublicShipment
{
    public string Status { get; init; } = "";
    public string CarrierName { get; init; } = "";
    public string ServiceName { get; init; } = "";
    public string? TrackingCode { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? ShippedAt { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
}

public sealed class CheckoutContact
{
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string Phone { get; init; } = "";
}

public sealed record CheckoutCreateRequest(IReadOnlyList<CartLine> Lines, CheckoutContact Contact, CheckoutFulfillment Fulfillment);

public sealed record CheckoutFulfillment(string Method, Guid? PublicPickupId = null, Guid? PublicShippingQuoteId = null, CheckoutAddress? ShippingAddress = null);

public sealed record ShippingQuoteRequest(IReadOnlyList<CartLine> Lines, string DestinationPostalCode);

public sealed record ShippingQuoteResult(CheckoutLoadState State, ShippingQuote? Quote, string? Message = null)
{
    public static ShippingQuoteResult Failure(CheckoutLoadState state, string? message = null) => new(state, null, message);
}

public sealed class ShippingQuote
{
    public DateTimeOffset ExpiresAt { get; init; }
    public string Currency { get; init; } = "BRL";
    public IReadOnlyList<ShippingQuoteOption> Options { get; init; } = [];
}

public sealed class ShippingQuoteOption
{
    public Guid PublicShippingQuoteId { get; init; }
    public string ServiceName { get; init; } = "";
    public string CarrierName { get; init; } = "";
    public decimal Price { get; init; }
    public int MinimumDeliveryDays { get; init; }
    public int MaximumDeliveryDays { get; init; }
}
