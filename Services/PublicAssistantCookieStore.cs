using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public sealed record PublicAssistantCredentials(Guid PublicId, string AccessToken, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

public interface IPublicAssistantCookieStore
{
    PublicAssistantCredentials? Read();
    bool Write(PublicAssistantCredentials credentials);
    bool Refresh(DateTimeOffset expiresAt);
    void Clear();
}

public sealed class PublicAssistantCookieStore(
    IHttpContextAccessor accessor,
    IDataProtectionProvider protection,
    IHostEnvironment environment,
    TimeProvider timeProvider) : IPublicAssistantCookieStore
{
    public const string CookieName = "morita_public_assistant";
    public const int MaxProtectedBytes = 2048;
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromDays(30);
    private readonly IDataProtector protector = protection.CreateProtector("Morita.LP.Razor.PublicAssistant.v1");

    public PublicAssistantCredentials? Read()
    {
        if (accessor.HttpContext is not { } context || !context.Request.Cookies.TryGetValue(CookieName, out var value)) return null;
        try
        {
            if (value.Length > MaxProtectedBytes) throw new InvalidDataException();
            var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(value));
            var now = timeProvider.GetUtcNow();
            if (payload is null || payload.Version != 1 || payload.PublicId == Guid.Empty || payload.IssuedAt > now.AddMinutes(1) || payload.ExpiresAt <= now || payload.ExpiresAt <= payload.IssuedAt || now - payload.IssuedAt > MaximumLifetime || payload.ExpiresAt - payload.IssuedAt > MaximumLifetime || payload.AccessToken.Length < 32 || payload.AccessToken.Length > 200) throw new InvalidDataException();
            return new(payload.PublicId, payload.AccessToken, payload.IssuedAt, payload.ExpiresAt);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException or FormatException or NullReferenceException)
        {
            Clear();
            return null;
        }
    }

    public bool Write(PublicAssistantCredentials credentials)
    {
        var now = timeProvider.GetUtcNow();
        if (credentials.PublicId == Guid.Empty || credentials.AccessToken is null || credentials.AccessToken.Length is < 32 or > 200 || credentials.IssuedAt > now.AddMinutes(1) || now - credentials.IssuedAt > MaximumLifetime || credentials.ExpiresAt <= now || credentials.ExpiresAt <= credentials.IssuedAt || credentials.ExpiresAt - credentials.IssuedAt > MaximumLifetime) return false;
        var protectedValue = protector.Protect(JsonSerializer.Serialize(new Payload(1, credentials.PublicId, credentials.AccessToken, credentials.IssuedAt, credentials.ExpiresAt)));
        if (protectedValue.Length > MaxProtectedBytes) return false;
        accessor.HttpContext?.Response.Cookies.Append(CookieName, protectedValue, CookieOptions(credentials.ExpiresAt));
        return true;
    }

    public bool Refresh(DateTimeOffset expiresAt)
    {
        var credentials = Read();
        var now = timeProvider.GetUtcNow();
        if (credentials is null) return false;
        var boundedExpiry = expiresAt > now.Add(MaximumLifetime) ? now.Add(MaximumLifetime) : expiresAt;
        return Write(credentials with { IssuedAt = now, ExpiresAt = boundedExpiry });
    }

    public void Clear() => accessor.HttpContext?.Response.Cookies.Delete(CookieName, CookieOptions(timeProvider.GetUtcNow().AddDays(1)));

    private CookieOptions CookieOptions(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = !environment.IsDevelopment() && !environment.IsEnvironment("E2E"),
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = "/",
        Expires = expires
    };

    private sealed record Payload(int Version, Guid PublicId, string AccessToken, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);
}
