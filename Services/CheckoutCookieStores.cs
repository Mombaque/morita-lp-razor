using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public sealed record DraftCredentials(string IdempotencyKey, string AccessToken, DateTimeOffset IssuedAt);
public sealed record CheckoutAccess(Guid PublicCheckoutId, string Token, DateTimeOffset IssuedAt, DateTimeOffset AccessExpiresAt);
public sealed record PaymentAttempt(Guid PublicCheckoutId, string IdempotencyKey, DateTimeOffset IssuedAt);
public sealed record OrderAccess(string PublicOrderNumber, string Token, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

public interface ICheckoutDraftCookieStore
{
    DraftCredentials Ensure();
    DraftCredentials? Read();
    void Clear();
}

public interface ICheckoutAccessCookieStore
{
    CheckoutAccess? Read(Guid publicCheckoutId);
    bool Write(CheckoutResponse checkout, string token);
    void Clear();
    // Forgets one checkout and keeps access to the others on this device.
    void Remove(Guid publicCheckoutId) => Clear();
}

public interface IPaymentAttemptCookieStore
{
    PaymentAttempt? Read(Guid publicCheckoutId);
    PaymentAttempt Ensure(Guid publicCheckoutId);
    PaymentAttempt Rotate(Guid publicCheckoutId);
    void Clear(Guid publicCheckoutId);
}

public interface IOrderAccessCookieStore
{
    OrderAccess? Read(string publicOrderNumber);
    bool Write(string publicOrderNumber, string token);
    void Clear();
}

public sealed class CheckoutDraftCookieStore(IHttpContextAccessor accessor, IDataProtectionProvider protection, IHostEnvironment environment, TimeProvider timeProvider) : ICheckoutDraftCookieStore
{
    public const string CookieName = "morita_checkout_draft";
    public const int MaxProtectedBytes = 2048;
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);
    private readonly IDataProtector protector = protection.CreateProtector("Morita.LP.Razor.CheckoutDraft.v1");
    public DraftCredentials Ensure()
    {
        return Read() ?? Create();
    }
    public DraftCredentials? Read()
    {
        if (accessor.HttpContext is not { } context || !context.Request.Cookies.TryGetValue(CookieName, out var value)) return null;
        try
        {
            if (value.Length > MaxProtectedBytes) throw new InvalidDataException();
            var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(value));
            var now = timeProvider.GetUtcNow();
            if (payload is null || payload.Version != 1 || payload.IssuedAt > now.AddMinutes(1) || now - payload.IssuedAt > Lifetime || string.IsNullOrWhiteSpace(payload.IdempotencyKey) || string.IsNullOrWhiteSpace(payload.AccessToken) || payload.IdempotencyKey.Length < 32 || payload.AccessToken.Length < 32 || payload.IdempotencyKey.Length > 200 || payload.AccessToken.Length > 200) throw new InvalidDataException();
            return new(payload.IdempotencyKey, payload.AccessToken, payload.IssuedAt);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException or FormatException)
        {
            context.Response.Cookies.Delete(CookieName, Options());
            return null;
        }
    }
    public void Clear() => accessor.HttpContext?.Response.Cookies.Delete(CookieName, Options());
    private DraftCredentials Create()
    {
        var value = new DraftCredentials(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), timeProvider.GetUtcNow());
        var protectedValue = protector.Protect(JsonSerializer.Serialize(new Payload(1, value.IssuedAt, value.IdempotencyKey, value.AccessToken)));
        if (protectedValue.Length > MaxProtectedBytes) throw new InvalidOperationException("Checkout draft cookie exceeds its limit.");
        accessor.HttpContext?.Response.Cookies.Append(CookieName, protectedValue, Options());
        return value;
    }
    private CookieOptions Options() => new() { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = !environment.IsDevelopment() && !environment.IsEnvironment("E2E"), IsEssential = true, Path = "/checkout", Expires = timeProvider.GetUtcNow().Add(Lifetime) };
    private sealed record Payload(int Version, DateTimeOffset IssuedAt, string IdempotencyKey, string AccessToken);
}

// Keeps the access tokens of this device's most recent checkouts, so starting a second checkout, or opening
// an older checkout link, never locks the shopper out of a reservation that is still waiting for payment.
// Only a cookie that fails decryption or validation is deleted; asking for another checkout is not tampering.
public sealed class CheckoutAccessCookieStore(IHttpContextAccessor accessor, IDataProtectionProvider protection, IHostEnvironment environment, TimeProvider timeProvider) : ICheckoutAccessCookieStore
{
    public const string CookieName = "morita_checkout_access";
    public const int MaxProtectedBytes = 2048;
    public const int MaxEntries = 3;
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault };
    private readonly IDataProtector protector = protection.CreateProtector("Morita.LP.Razor.CheckoutAccess.v1");

    public CheckoutAccess? Read(Guid publicCheckoutId) =>
        publicCheckoutId == Guid.Empty ? null : ReadAll().FirstOrDefault(entry => entry.PublicCheckoutId == publicCheckoutId);

    public bool Write(CheckoutResponse checkout, string token)
    {
        var now = timeProvider.GetUtcNow();
        if (checkout.PublicCheckoutId == Guid.Empty || string.IsNullOrWhiteSpace(token) || token.Length < 32 || token.Length > 200 || checkout.AccessExpiresAt <= now) return false;
        var entries = ReadAll()
            .Where(entry => entry.PublicCheckoutId != checkout.PublicCheckoutId)
            .Prepend(new CheckoutAccess(checkout.PublicCheckoutId, token, now, checkout.AccessExpiresAt))
            .Take(MaxEntries)
            .ToList();
        // The newest checkout must always fit; older ones are dropped first when the cookie would grow too large.
        while (entries.Count > 0)
        {
            if (TryAppend(entries)) return true;
            if (entries.Count == 1) return false;
            entries.RemoveAt(entries.Count - 1);
        }
        return false;
    }

    public void Remove(Guid publicCheckoutId)
    {
        var entries = ReadAll();
        var remaining = entries.Where(entry => entry.PublicCheckoutId != publicCheckoutId).ToList();
        if (remaining.Count == entries.Count) return;
        if (remaining.Count == 0 || !TryAppend(remaining)) Clear();
    }

    public void Clear() => accessor.HttpContext?.Response.Cookies.Delete(CookieName, Options());

    private List<CheckoutAccess> ReadAll()
    {
        if (accessor.HttpContext is not { } context || !context.Request.Cookies.TryGetValue(CookieName, out var value)) return [];
        try
        {
            if (value.Length > MaxProtectedBytes) throw new InvalidDataException();
            var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(value), ReadOptions) ?? throw new InvalidDataException();
            // Version 1 cookies, written before this store kept several checkouts, hold a single entry at the top level.
            List<Entry?> stored = payload.Version switch
            {
                1 => [new Entry(payload.PublicCheckoutId, payload.Token, payload.IssuedAt, payload.AccessExpiresAt)],
                2 when payload.Entries is { Count: > 0 and <= MaxEntries } list => list,
                _ => throw new InvalidDataException()
            };
            var now = timeProvider.GetUtcNow();
            var valid = stored.Where(entry => Valid(entry, now)).Select(entry => new CheckoutAccess(entry!.PublicCheckoutId, entry.Token!, entry.IssuedAt, entry.AccessExpiresAt)).ToList();
            if (valid.Count == 0) throw new InvalidDataException();
            return valid;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException or FormatException)
        {
            context.Response.Cookies.Delete(CookieName, Options());
            return [];
        }
    }

    private static bool Valid(Entry? entry, DateTimeOffset now) =>
        entry is not null && entry.PublicCheckoutId != Guid.Empty && entry.IssuedAt <= now.AddMinutes(1) && now - entry.IssuedAt <= TimeSpan.FromDays(30) &&
        entry.AccessExpiresAt >= now && !string.IsNullOrWhiteSpace(entry.Token) && entry.Token.Length is >= 32 and <= 200;

    private bool TryAppend(IReadOnlyList<CheckoutAccess> entries)
    {
        var value = protector.Protect(JsonSerializer.Serialize(new Payload(2, Guid.Empty, null, default, default, entries.Select(entry => (Entry?)new Entry(entry.PublicCheckoutId, entry.Token, entry.IssuedAt, entry.AccessExpiresAt)).ToList()), WriteOptions));
        if (value.Length > MaxProtectedBytes) return false;
        accessor.HttpContext?.Response.Cookies.Append(CookieName, value, Options(entries.Max(entry => entry.AccessExpiresAt)));
        return true;
    }

    private CookieOptions Options(DateTimeOffset? expires = null) => new() { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = !environment.IsDevelopment() && !environment.IsEnvironment("E2E"), IsEssential = true, Path = "/checkout", Expires = expires ?? timeProvider.GetUtcNow().AddDays(30) };
    private sealed record Payload(int Version, Guid PublicCheckoutId, string? Token, DateTimeOffset IssuedAt, DateTimeOffset AccessExpiresAt, List<Entry?>? Entries = null);
    private sealed record Entry(Guid PublicCheckoutId, string? Token, DateTimeOffset IssuedAt, DateTimeOffset AccessExpiresAt);
}

public sealed class PaymentAttemptCookieStore(IHttpContextAccessor accessor, IDataProtectionProvider protection, IHostEnvironment environment, TimeProvider timeProvider) : IPaymentAttemptCookieStore
{
    public const string CookieName = "morita_payment_attempt";
    public const int MaxProtectedBytes = 2048;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);
    private readonly IDataProtector protector = protection.CreateProtector("Morita.LP.Razor.PaymentAttempt.v1");
    public PaymentAttempt? Read(Guid checkoutId)
    {
        if (accessor.HttpContext is not { } context || !context.Request.Cookies.TryGetValue(CookieName, out var value)) return null;
        try
        {
            if (value.Length > MaxProtectedBytes) throw new InvalidDataException();
            var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(value));
            var now = timeProvider.GetUtcNow();
            if (payload is null || payload.Version != 1 || payload.PublicCheckoutId == Guid.Empty || payload.IssuedAt > now.AddMinutes(1) || now - payload.IssuedAt > Lifetime || payload.IdempotencyKey.Length < 32 || payload.IdempotencyKey.Length > 128) throw new InvalidDataException();
            // An attempt for another checkout is still valid for that checkout; leave it in place.
            return payload.PublicCheckoutId == checkoutId ? new(checkoutId, payload.IdempotencyKey, payload.IssuedAt) : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException or FormatException or NullReferenceException)
        { context.Response.Cookies.Delete(CookieName, Options()); return null; }
    }
    public PaymentAttempt Ensure(Guid checkoutId) => Read(checkoutId) ?? Create(checkoutId);
    public PaymentAttempt Rotate(Guid checkoutId) => Create(checkoutId);
    public void Clear(Guid checkoutId) => accessor.HttpContext?.Response.Cookies.Delete(CookieName, Options());
    private PaymentAttempt Create(Guid checkoutId)
    {
        var attempt = new PaymentAttempt(checkoutId, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), timeProvider.GetUtcNow());
        var protectedValue = protector.Protect(JsonSerializer.Serialize(new Payload(1, checkoutId, attempt.IdempotencyKey, attempt.IssuedAt)));
        if (protectedValue.Length > MaxProtectedBytes) throw new InvalidOperationException("Payment attempt cookie exceeds its limit.");
        accessor.HttpContext?.Response.Cookies.Append(CookieName, protectedValue, Options());
        return attempt;
    }
    private CookieOptions Options() => new() { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = !environment.IsDevelopment() && !environment.IsEnvironment("E2E"), IsEssential = true, Path = "/checkout", Expires = timeProvider.GetUtcNow().Add(Lifetime) };
    private sealed record Payload(int Version, Guid PublicCheckoutId, string IdempotencyKey, DateTimeOffset IssuedAt);
}

public sealed class OrderAccessCookieStore(IHttpContextAccessor accessor, IDataProtectionProvider protection, IHostEnvironment environment, TimeProvider timeProvider) : IOrderAccessCookieStore
{
    public const string CookieName = "morita_order_access";
    public const int MaxProtectedBytes = 2048;
    private readonly IDataProtector protector = protection.CreateProtector("Morita.LP.Razor.OrderAccess.v1");
    public OrderAccess? Read(string publicOrderNumber)
    {
        if (!IsValidOrderNumber(publicOrderNumber) || accessor.HttpContext is not { } context || !context.Request.Cookies.TryGetValue(CookieName, out var value)) return null;
        try
        {
            if (value.Length > MaxProtectedBytes) throw new InvalidDataException();
            var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(value));
            var now = timeProvider.GetUtcNow();
            if (payload is null || payload.Version != 1 || !IsValidOrderNumber(payload.PublicOrderNumber ?? "") || payload.IssuedAt > now.AddMinutes(1) || payload.ExpiresAt < now || payload.Token.Length < 32 || payload.Token.Length > 200) throw new InvalidDataException();
            // Opening another order's link must not revoke this device's access to the order it holds.
            return string.Equals(payload.PublicOrderNumber, publicOrderNumber, StringComparison.OrdinalIgnoreCase) ? new(payload.PublicOrderNumber!, payload.Token, payload.IssuedAt, payload.ExpiresAt) : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException or FormatException or NullReferenceException)
        { context.Response.Cookies.Delete(CookieName, Options()); return null; }
    }
    public bool Write(string publicOrderNumber, string token)
    {
        if (!IsValidOrderNumber(publicOrderNumber) || token.Length < 32 || token.Length > 200) return false;
        var now = timeProvider.GetUtcNow();
        var expiry = now.AddDays(90);
        if (expiry <= now) return false;
        var protectedValue = protector.Protect(JsonSerializer.Serialize(new Payload(1, publicOrderNumber.ToUpperInvariant(), token, now, expiry)));
        if (protectedValue.Length > MaxProtectedBytes) return false;
        accessor.HttpContext?.Response.Cookies.Append(CookieName, protectedValue, Options(expiry)); return true;
    }
    public void Clear() => accessor.HttpContext?.Response.Cookies.Delete(CookieName, Options());
    private CookieOptions Options(DateTimeOffset? expires = null) => new() { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = !environment.IsDevelopment() && !environment.IsEnvironment("E2E"), IsEssential = true, Path = "/order", Expires = expires ?? timeProvider.GetUtcNow().AddDays(90) };
    public static bool IsValidOrderNumber(string value) => value.Length == 19 && value.StartsWith("MF-", StringComparison.OrdinalIgnoreCase) && value.Skip(3).All(c => "0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(char.ToUpperInvariant(c)));
    private sealed record Payload(int Version, string PublicOrderNumber, string Token, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);
}

public sealed class CheckoutRateLimiter(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset Start, int Count)> attempts = new();
    private const int MaximumEntries = 4096;
    public bool TryConsume(string client, string operation)
    {
        var key = operation + ":" + client;
        var now = timeProvider.GetUtcNow();
        if (attempts.Count > MaximumEntries)
        {
            foreach (var entry in attempts)
                if (now - entry.Value.Start >= TimeSpan.FromMinutes(1)) attempts.TryRemove(entry.Key, out _);
        }
        if (!attempts.ContainsKey(key) && attempts.Count >= MaximumEntries)
            return false;
        while (true)
        {
            var current = attempts.GetOrAdd(key, (now, 0));
            if (now - current.Start >= TimeSpan.FromMinutes(1)) { if (attempts.TryUpdate(key, (now, 1), current)) return true; continue; }
            if (current.Count >= 12) return false;
            if (attempts.TryUpdate(key, (current.Start, current.Count + 1), current)) return true;
        }
    }
}
