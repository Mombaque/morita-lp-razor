using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Morita.LP.Razor.Services;

public sealed record CustomerAccountSession(string Token, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

public interface ICustomerAccountCookieStore
{
    CustomerAccountSession? Read();
    bool Write(string token, DateTimeOffset expiresAt);
    void Clear();
}

public sealed class CustomerAccountCookieStore(IHttpContextAccessor accessor, IDataProtectionProvider protection, IHostEnvironment environment, TimeProvider timeProvider) : ICustomerAccountCookieStore
{
    public const string CookieName = "morita_customer_session";
    public const int MaxProtectedBytes = 2048;
    private readonly IDataProtector protector = protection.CreateProtector("Morita.LP.Razor.CustomerAccountSession.v1");

    public CustomerAccountSession? Read()
    {
        if (accessor.HttpContext is not { } context || !context.Request.Cookies.TryGetValue(CookieName, out var value)) return null;
        try
        {
            if (value.Length > MaxProtectedBytes) throw new InvalidDataException();
            var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(value));
            var now = timeProvider.GetUtcNow();
            if (payload is null || payload.Version != 1 || payload.IssuedAt > now.AddMinutes(1) || payload.ExpiresAt <= now || payload.ExpiresAt <= payload.IssuedAt || payload.ExpiresAt > payload.IssuedAt.AddDays(91) || payload.Token.Length is < 32 or > 200) throw new InvalidDataException();
            return new(payload.Token, payload.IssuedAt, payload.ExpiresAt);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException or FormatException or NullReferenceException)
        { Clear(); return null; }
    }

    public bool Write(string token, DateTimeOffset expiresAt)
    {
        var now = timeProvider.GetUtcNow();
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 32 or > 200 || expiresAt <= now || expiresAt > now.AddDays(91)) return false;
        var value = protector.Protect(JsonSerializer.Serialize(new Payload(1, token, now, expiresAt)));
        if (value.Length > MaxProtectedBytes) return false;
        accessor.HttpContext?.Response.Cookies.Append(CookieName, value, Options(expiresAt));
        return true;
    }

    public void Clear() => accessor.HttpContext?.Response.Cookies.Delete(CookieName, Options());
    private CookieOptions Options(DateTimeOffset? expires = null) => new() { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = !environment.IsDevelopment() && !environment.IsEnvironment("E2E"), IsEssential = true, Path = "/", Expires = expires ?? timeProvider.GetUtcNow() };
    private sealed record Payload(int Version, string Token, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);
}
