using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class PublicAssistantCookieStoreTests
{
    [Fact]
    public void Writes_protected_http_only_lax_cookie_without_exposing_token()
    {
        var context = new DefaultHttpContext();
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), builder => builder.SetApplicationName("Morita.LP.Razor"));
        var store = new PublicAssistantCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestHostEnvironment("Production"), TimeProvider.System);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        Assert.True(store.Write(new PublicAssistantCredentials(id, token, now, now.AddDays(30))));
        var setCookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("HttpOnly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, setCookie);
        Assert.Contains(PublicAssistantCookieStore.CookieName, setCookie);
    }

    [Fact]
    public void Protected_cookie_survives_provider_restart_with_persisted_keys()
    {
        var directory = Directory.CreateTempSubdirectory("morita-assistant-keys-");
        try
        {
            var now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var id = Guid.NewGuid();
            var firstContext = new DefaultHttpContext();
            var firstProvider = DataProtectionProvider.Create(directory, builder => builder.SetApplicationName("Morita.LP.Razor"));
            var firstStore = new PublicAssistantCookieStore(new HttpContextAccessor { HttpContext = firstContext }, firstProvider, new TestHostEnvironment("Production"), new FixedTimeProvider(now));

            Assert.True(firstStore.Write(new PublicAssistantCredentials(id, token, now, now.AddDays(30))));
            var cookie = firstContext.Response.Headers.SetCookie.ToString().Split(';', 2)[0];

            var secondContext = new DefaultHttpContext();
            secondContext.Request.Headers.Cookie = cookie;
            var secondProvider = DataProtectionProvider.Create(directory, builder => builder.SetApplicationName("Morita.LP.Razor"));
            var secondStore = new PublicAssistantCookieStore(new HttpContextAccessor { HttpContext = secondContext }, secondProvider, new TestHostEnvironment("Production"), new FixedTimeProvider(now.AddDays(1)));

            var restored = secondStore.Read();
            Assert.NotNull(restored);
            Assert.Equal(id, restored.PublicId);
            Assert.Equal(token, restored.AccessToken);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Rejects_cookie_lifetime_beyond_thirty_days()
    {
        var now = DateTimeOffset.Parse("2026-08-21T12:00:00Z");
        var context = new DefaultHttpContext();
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), builder => builder.SetApplicationName("Morita.LP.Razor"));
        var store = new PublicAssistantCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestHostEnvironment("Production"), new FixedTimeProvider(now));

        Assert.False(store.Write(new PublicAssistantCredentials(Guid.NewGuid(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), now, now.AddDays(30).AddSeconds(1))));
    }

    [Fact]
    public void Invalid_cookie_is_cleared_and_cannot_authorize()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{PublicAssistantCookieStore.CookieName}=invalid";
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), builder => builder.SetApplicationName("Morita.LP.Razor"));
        var store = new PublicAssistantCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestHostEnvironment("Production"), TimeProvider.System);

        Assert.Null(store.Read());
        Assert.Contains(PublicAssistantCookieStore.CookieName, context.Response.Headers.SetCookie.ToString());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
