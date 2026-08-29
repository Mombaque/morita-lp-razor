namespace Morita.LP.Razor.Models;

public sealed class CustomerAccountProfile
{
    public Guid AccountId { get; init; }
    public string Email { get; init; } = "";
    public string? Name { get; init; }
    public string? Phone { get; init; }
    public CustomerAccountAddress? Address { get; init; }
}

public sealed class CustomerAccountAddress
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

public sealed record AccountCodeChallenge(Guid ChallengeId, DateTimeOffset ExpiresAt);
public enum AccountLoadState { Success, Validation, Unauthorized, NotFound, Conflict, RateLimited, Unavailable, Timeout, Malformed }
public sealed record AccountResult<T>(AccountLoadState State, T? Value, string? Message = null)
{
    public static AccountResult<T> Failure(AccountLoadState state, string? message = null) => new(state, default, message);
}
