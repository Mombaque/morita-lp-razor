using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Models;

public enum CheckoutLoadState { Success, Validation, Conflict, NotFound, RateLimited, Unavailable, Timeout, Malformed }

public sealed record CheckoutResult(CheckoutLoadState State, CheckoutResponse? Checkout, string? Message = null)
{
    public static CheckoutResult Failure(CheckoutLoadState state, string? message = null) => new(state, null, message);
}

public sealed record CheckoutConfigurationResult(CheckoutLoadState State, CheckoutConfiguration? Configuration)
{
    public static CheckoutConfigurationResult Failure(CheckoutLoadState state) => new(state, null);
}

public sealed class CheckoutConfiguration
{
    public bool PickupEnabled { get; init; }
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
    public PickupSnapshot Pickup { get; init; } = new();
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
    public string Street { get; init; } = "";
    public string Number { get; init; } = "";
    public string? Complement { get; init; }
    public string Neighborhood { get; init; } = "";
    public string City { get; init; } = "";
    public string State { get; init; } = "";
    public string PostalCode { get; init; } = "";
}

public sealed class CheckoutContact
{
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string Phone { get; init; } = "";
}

public sealed record CheckoutCreateRequest(IReadOnlyList<CartLine> Lines, CheckoutContact Contact, Guid PublicPickupId);
