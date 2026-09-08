namespace Morita.LP.Razor.Models;

public sealed class CustomerAccountProfile
{
    public Guid AccountId { get; init; }
    public string Email { get; init; } = "";
    public string? Name { get; init; }
    public string? Phone { get; init; }
}

public sealed class CustomerAccountAddress
{
    public Guid PublicAddressId { get; init; }
    public string Label { get; init; } = "";
    public bool IsDefault { get; init; }
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

public sealed record AccountCodeChallenge(Guid ChallengeId, DateTimeOffset ExpiresAt, string? PrivacyPolicyVersion = null);
public enum AccountLoadState { Success, Validation, Unauthorized, NotFound, Conflict, RateLimited, Unavailable, Timeout, Malformed }
public sealed record AccountResult<T>(AccountLoadState State, T? Value, string? Message = null)
{
    public static AccountResult<T> Failure(AccountLoadState state, string? message = null) => new(state, default, message);
}

public sealed class StorefrontAccountOrderSummary
{
    public string PublicOrderNumber { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "BRL";
    public string PaymentStatus { get; init; } = "";
    public string FulfillmentStatus { get; init; } = "";
    public string FulfillmentMethod { get; init; } = "";
    public string? RepresentativeProductPresentation { get; init; }
    public string? RepresentativeProductImageUrl { get; init; }
}

public sealed class StorefrontAccountOrderPage
{
    public IReadOnlyList<StorefrontAccountOrderSummary> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}
