using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Morita.LP.Razor.Services;

public sealed record CartLine(Guid PublicOfferId, int Quantity);
public sealed record CartState(DateTimeOffset IssuedAt, IReadOnlyList<CartLine> Lines);

public interface ICartCookieStore
{
    CartState Read();
    bool Add(Guid offerId, int quantity);
    bool Update(Guid offerId, int quantity);
    bool Remove(Guid offerId);
    void Clear();
}

/// <summary>Opaque, authenticated cart persistence. Catalog data never enters the cookie.</summary>
public sealed class CartCookieStore(
    IHttpContextAccessor accessor,
    IDataProtectionProvider protection,
    IHostEnvironment environment,
    TimeProvider timeProvider) : ICartCookieStore
{
    public const string CookieName = "morita_cart";
    public const int MaxLines = 20;
    public const int MaxUnitsPerLine = 10;
    public const int MaxTotalUnits = 200;
    public const int MaxProtectedBytes = 3072;
    private const int Version = 1;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _protector = protection.CreateProtector("Morita.LP.Razor.Cart.v1");
    private readonly TimeProvider _timeProvider = timeProvider;

    public CartState Read()
    {
        var context = accessor.HttpContext;
        if (context is null || !context.Request.Cookies.TryGetValue(CookieName, out var value))
            return new(_timeProvider.GetUtcNow(), []);
        try
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxProtectedBytes)
                throw new InvalidDataException();
            var payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(value), JsonOptions);
            if (payload is null || payload.Version != Version || payload.IssuedAt is null ||
                payload.IssuedAt.Value > _timeProvider.GetUtcNow().AddMinutes(1) || _timeProvider.GetUtcNow() - payload.IssuedAt.Value > Lifetime || payload.Lines is null ||
                payload.Lines.Count > MaxLines || payload.Lines.Any(x => x is null || x.PublicOfferId == Guid.Empty || x.Quantity is < 1 or > MaxUnitsPerLine) ||
                payload.Lines.GroupBy(x => x.PublicOfferId).Any(g => g.Count() != 1) || payload.Lines.Sum(x => x.Quantity) > MaxTotalUnits)
                throw new InvalidDataException();
            return new(payload.IssuedAt.Value, payload.Lines.Select(x => new CartLine(x.PublicOfferId, x.Quantity)).ToList());
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException or FormatException)
        {
            context.Response.Cookies.Delete(CookieName, CookieOptions());
            return new(_timeProvider.GetUtcNow(), []);
        }
    }

    public bool Add(Guid offerId, int quantity)
    {
        var state = Read();
        var lines = state.Lines.ToList();
        var index = lines.FindIndex(x => x.PublicOfferId == offerId);
        var next = (index < 0 ? 0 : lines[index].Quantity) + quantity;
        if (offerId == Guid.Empty || quantity < 1 || next > MaxUnitsPerLine || (index < 0 && lines.Count >= MaxLines) || state.Lines.Sum(x => x.Quantity) + quantity > MaxTotalUnits) return false;
        if (index < 0) lines.Add(new(offerId, quantity)); else lines[index] = new(offerId, next);
        return Write(new(_timeProvider.GetUtcNow(), lines));
    }

    public bool Update(Guid offerId, int quantity)
    {
        var state = Read();
        if (quantity is < 1 or > MaxUnitsPerLine) return false;
        var lines = state.Lines.ToList();
        var index = lines.FindIndex(x => x.PublicOfferId == offerId);
        if (index < 0 || lines.Sum(x => x.Quantity) - lines[index].Quantity + quantity > MaxTotalUnits) return false;
        lines[index] = new(offerId, quantity);
        return Write(new(_timeProvider.GetUtcNow(), lines));
    }

    public bool Remove(Guid offerId)
    {
        var state = Read();
        var lines = state.Lines.Where(x => x.PublicOfferId != offerId).ToList();
        return lines.Count != state.Lines.Count && Write(new(_timeProvider.GetUtcNow(), lines));
    }

    public void Clear() => accessor.HttpContext?.Response.Cookies.Delete(CookieName, CookieOptions());

    private bool Write(CartState state)
    {
        var context = accessor.HttpContext;
        if (context is null) return false;
        var protectedValue = _protector.Protect(JsonSerializer.Serialize(new Payload(Version, state.IssuedAt, state.Lines.Select(x => new PayloadLine(x.PublicOfferId, x.Quantity)).ToList()), JsonOptions));
        if (protectedValue.Length > MaxProtectedBytes) return false;
        context.Response.Cookies.Append(CookieName, protectedValue, CookieOptions());
        return true;
    }

    private CookieOptions CookieOptions() => new()
    {
        HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", IsEssential = true,
        Secure = !environment.IsDevelopment() && !environment.IsEnvironment("E2E"),
        Expires = _timeProvider.GetUtcNow().Add(Lifetime)
    };

    private sealed record Payload(int Version, DateTimeOffset? IssuedAt, List<PayloadLine>? Lines);
    private sealed record PayloadLine(Guid PublicOfferId, int Quantity);
}
