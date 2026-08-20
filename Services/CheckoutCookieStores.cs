using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public sealed record DraftCredentials(string IdempotencyKey, string AccessToken, DateTimeOffset IssuedAt);
public sealed record CheckoutAccess(Guid PublicCheckoutId, string Token, DateTimeOffset IssuedAt, DateTimeOffset AccessExpiresAt);

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

public sealed class CheckoutAccessCookieStore(IHttpContextAccessor accessor, IDataProtectionProvider protection, IHostEnvironment environment, TimeProvider timeProvider) : ICheckoutAccessCookieStore
{
    public const string CookieName = "morita_checkout_access";
    public const int MaxProtectedBytes = 2048;
    private readonly IDataProtector protector = protection.CreateProtector("Morita.LP.Razor.CheckoutAccess.v1");
    public CheckoutAccess? Read(Guid publicCheckoutId)
    {
        if (accessor.HttpContext is not { } context || !context.Request.Cookies.TryGetValue(CookieName, out var value)) return null;
        try
        {
            if (value.Length > MaxProtectedBytes) throw new InvalidDataException();
            var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(value));
            var now = timeProvider.GetUtcNow();
            if (payload is null || payload.Version != 1 || payload.PublicCheckoutId != publicCheckoutId || payload.IssuedAt > now.AddMinutes(1) || now - payload.IssuedAt > TimeSpan.FromDays(30) || payload.AccessExpiresAt < now || string.IsNullOrWhiteSpace(payload.Token) || payload.Token.Length < 32 || payload.Token.Length > 200) throw new InvalidDataException();
            return new(payload.PublicCheckoutId, payload.Token, payload.IssuedAt, payload.AccessExpiresAt);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException or FormatException)
        {
            context.Response.Cookies.Delete(CookieName, Options());
            return null;
        }
    }
    public bool Write(CheckoutResponse checkout, string token)
    {
        if (checkout.PublicCheckoutId == Guid.Empty || string.IsNullOrWhiteSpace(token) || token.Length < 32 || token.Length > 200 || checkout.AccessExpiresAt <= timeProvider.GetUtcNow()) return false;
        var issued = timeProvider.GetUtcNow();
        var value = protector.Protect(JsonSerializer.Serialize(new Payload(1, checkout.PublicCheckoutId, token, issued, checkout.AccessExpiresAt)));
        if (value.Length > MaxProtectedBytes) return false;
        accessor.HttpContext?.Response.Cookies.Append(CookieName, value, Options(checkout.AccessExpiresAt));
        return true;
    }
    public void Clear() => accessor.HttpContext?.Response.Cookies.Delete(CookieName, Options());
    private CookieOptions Options(DateTimeOffset? expires = null) => new() { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = !environment.IsDevelopment() && !environment.IsEnvironment("E2E"), IsEssential = true, Path = "/checkout", Expires = expires ?? timeProvider.GetUtcNow().AddDays(30) };
    private sealed record Payload(int Version, Guid PublicCheckoutId, string Token, DateTimeOffset IssuedAt, DateTimeOffset AccessExpiresAt);
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
