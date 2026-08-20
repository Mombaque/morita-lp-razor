using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class CartCookieStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Round_trip_contains_only_opaque_authoritative_fields()
    {
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var context = new DefaultHttpContext();
        var store = Create(provider, context);
        var offer = Guid.NewGuid();

        Assert.True(store.Add(offer, 2));
        var value = CookieValue(context);
        context.Request.Headers.Cookie = $"{CartCookieStore.CookieName}={value}";
        var protector = provider.CreateProtector("Morita.LP.Razor.Cart.v1");
        using var json = JsonDocument.Parse(protector.Unprotect(value));
        Assert.Equal(new[] { "version", "issuedAt", "lines" }, json.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.Equal(new[] { "publicOfferId", "quantity" }, json.RootElement.GetProperty("lines")[0].EnumerateObject().Select(x => x.Name));
        Assert.Equal(offer, store.Read().Lines[0].PublicOfferId);
        Assert.Equal(2, store.Read().Lines[0].Quantity);
        Assert.DoesNotContain("price", protector.Unprotect(value), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("image", protector.Unprotect(value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tampered_malformed_unsupported_expired_and_future_cookies_are_discarded()
    {
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        foreach (var value in new[] { "tampered", "not-json" })
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Cookie = $"{CartCookieStore.CookieName}={value}";
            Assert.Empty(Create(provider, context).Read().Lines);
            Assert.Contains(CartCookieStore.CookieName, context.Response.Headers.SetCookie.ToString());
        }

        foreach (var issuedAt in new[] { Now.AddDays(-31), Now.AddDays(1) })
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Cookie = $"{CartCookieStore.CookieName}={Protect(provider, new { version = 99, issuedAt, lines = Array.Empty<object>() })}";
            Assert.Empty(Create(provider, context).Read().Lines);
            context = new DefaultHttpContext();
            context.Request.Headers.Cookie = $"{CartCookieStore.CookieName}={Protect(provider, new { version = 1, issuedAt, lines = Array.Empty<object>() })}";
            Assert.Empty(Create(provider, context).Read().Lines);
        }
    }

    [Fact]
    public void Merge_update_remove_clear_and_limits_are_enforced()
    {
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var context = new DefaultHttpContext();
        var store = Create(provider, context);
        var first = Guid.NewGuid();
        Assert.True(store.Add(first, 6));
        CarryCookie(context);
        Assert.True(store.Add(first, 4));
        CarryCookie(context);
        Assert.False(store.Add(first, 1));
        Assert.True(store.Update(first, 3));
        CarryCookie(context);
        Assert.True(store.Remove(first));
        CarryCookie(context);
        Assert.False(store.Remove(first));
        Assert.True(store.Add(first, 1));
        store.Clear();
        Assert.Contains("expires=Thu, 01 Jan 1970", context.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);

        context = new DefaultHttpContext();
        store = Create(provider, context);
        for (var i = 0; i < CartCookieStore.MaxLines; i++)
        {
            Assert.True(store.Add(Guid.NewGuid(), 1));
            if (i < CartCookieStore.MaxLines - 1) CarryCookie(context);
        }
        context.Request.Headers.Cookie = $"{CartCookieStore.CookieName}={CookieValue(context)}";
        Assert.Equal(CartCookieStore.MaxLines, store.Read().Lines.Count);
        Assert.True(CookieValue(context).Length <= CartCookieStore.MaxProtectedBytes);
        Assert.False(store.Add(Guid.NewGuid(), 1));
    }

    [Fact]
    public void Cookie_attributes_and_twenty_lines_fit_after_provider_restart()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var firstProvider = DataProtectionProvider.Create(directory, c => c.SetApplicationName("Morita.LP.Razor"));
            var context = new DefaultHttpContext();
            var store = Create(firstProvider, context, "Production");
            for (var i = 0; i < 20; i++)
            {
                Assert.True(store.Add(Guid.NewGuid(), 1));
                if (i < 19) CarryCookie(context);
            }
            var setCookie = context.Response.Headers.SetCookie.ToString();
            Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);

            var secondProvider = DataProtectionProvider.Create(directory, c => c.SetApplicationName("Morita.LP.Razor"));
            context = new DefaultHttpContext();
            context.Request.Headers.Cookie = $"{CartCookieStore.CookieName}={CookieValueFrom(setCookie)}";
            Assert.Equal(20, Create(secondProvider, context, "Production").Read().Lines.Count);
        }
        finally { directory.Delete(true); }
    }

    private static CartCookieStore Create(IDataProtectionProvider provider, HttpContext context, string environment = "Development") =>
        new(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(environment), new FixedTimeProvider(Now));

    private static string CookieValue(HttpContext context) => CookieValueFrom(context.Response.Headers.SetCookie.ToString());
    private static void CarryCookie(HttpContext context)
    {
        context.Request.Headers.Cookie = $"{CartCookieStore.CookieName}={CookieValue(context)}";
        context.Response.Headers.Remove("Set-Cookie");
    }
    private static string CookieValueFrom(string header) => header.Split(';', 2)[0].Split('=', 2)[1];
    private static string Protect(IDataProtectionProvider provider, object value) => provider.CreateProtector("Morita.LP.Razor.Cart.v1").Protect(JsonSerializer.Serialize(value));

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ApplicationVersion { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
