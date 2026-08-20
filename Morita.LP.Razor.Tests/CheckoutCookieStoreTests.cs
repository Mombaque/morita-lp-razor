using System;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class CheckoutCookieStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Draft_credentials_are_stable_and_protected_across_restart()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var first = DataProtectionProvider.Create(directory, c => c.SetApplicationName("Morita.LP.Razor"));
            var context = new DefaultHttpContext();
            var store = Draft(first, context, "Production");
            var credentials = store.Ensure();
            var cookie = CookieValue(context, CheckoutDraftCookieStore.CookieName);
            context.Request.Headers.Cookie = $"{CheckoutDraftCookieStore.CookieName}={cookie}";
            Assert.Equal(credentials, store.Ensure());
            Assert.True(credentials.IdempotencyKey.Length >= 32);
            Assert.True(credentials.AccessToken.Length >= 32);
            Assert.Contains("httponly", context.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", context.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", context.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);

            var second = DataProtectionProvider.Create(directory, c => c.SetApplicationName("Morita.LP.Razor"));
            var restarted = new DefaultHttpContext();
            restarted.Request.Headers.Cookie = $"{CheckoutDraftCookieStore.CookieName}={cookie}";
            Assert.Equal(credentials, Draft(second, restarted, "Production").Read());
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void Access_cookie_rejects_tampered_future_expired_unsupported_oversized_and_null_tokens()
    {
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var checkoutId = Guid.NewGuid();
        foreach (var value in new[] { "tampered", new string('x', CheckoutAccessCookieStore.MaxProtectedBytes + 1), Protect(provider, new { version = 1, publicCheckoutId = checkoutId, token = (string?)null, issuedAt = Now, accessExpiresAt = Now.AddDays(1) }), Protect(provider, new { version = 99, publicCheckoutId = checkoutId, token = new string('a', 32), issuedAt = Now, accessExpiresAt = Now.AddDays(1) }), Protect(provider, new { version = 1, publicCheckoutId = checkoutId, token = new string('a', 32), issuedAt = Now.AddMinutes(2), accessExpiresAt = Now.AddDays(1) }), Protect(provider, new { version = 1, publicCheckoutId = checkoutId, token = new string('a', 32), issuedAt = Now.AddDays(-31), accessExpiresAt = Now.AddDays(-30) }) })
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Cookie = $"{CheckoutAccessCookieStore.CookieName}={value}";
            Assert.Null(Access(provider, context).Read(checkoutId));
            Assert.Contains(CheckoutAccessCookieStore.CookieName, context.Response.Headers.SetCookie.ToString());
        }
    }

    [Fact]
    public void Access_cookie_round_trips_and_rejects_wrong_checkout()
    {
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var context = new DefaultHttpContext();
        var store = Access(provider, context, "Production");
        var checkout = new CheckoutResponse { PublicCheckoutId = Guid.NewGuid(), ExpiresAt = Now.AddMinutes(30), AccessExpiresAt = Now.AddDays(30), Currency = "BRL", Total = 10, MerchandiseTotal = 10, Pickup = Pickup(), Lines = [new CheckoutLine { PublicOfferId = Guid.NewGuid(), Quantity = 1, UnitPrice = 10, LineTotal = 10 }] };
        var token = new string('t', 32);
        Assert.True(store.Write(checkout, token));
        var cookie = CookieValue(context, CheckoutAccessCookieStore.CookieName);
        context.Request.Headers.Cookie = $"{CheckoutAccessCookieStore.CookieName}={cookie}";
        Assert.Equal(token, store.Read(checkout.PublicCheckoutId)?.Token);
        Assert.Null(store.Read(Guid.NewGuid()));
    }

    private static CheckoutDraftCookieStore Draft(IDataProtectionProvider provider, HttpContext context, string environment = "Development") => new(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(environment), new FixedTimeProvider(Now));
    private static CheckoutAccessCookieStore Access(IDataProtectionProvider provider, HttpContext context, string environment = "Development") => new(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(environment), new FixedTimeProvider(Now));
    private static string CookieValue(HttpContext context, string name) => context.Response.Headers.SetCookie.ToString().Split(';', 2)[0].Split('=', 2)[1];
    private static string Protect(IDataProtectionProvider provider, object value) => provider.CreateProtector("Morita.LP.Razor.CheckoutAccess.v1").Protect(JsonSerializer.Serialize(value));
    private static PickupSnapshot Pickup() => new() { PublicPickupId = Guid.NewGuid(), DisplayName = "Loja", Address = new() { Street = "Rua", Number = "1", Neighborhood = "Centro", City = "Sorocaba", State = "SP", PostalCode = "18000-000" } };

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider { public override DateTimeOffset GetUtcNow() => value; }
    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ApplicationVersion { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
