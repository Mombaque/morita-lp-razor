using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Pages;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class CustomerAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Session_cookie_round_trips_across_persisted_keys_and_rejects_tampering_and_expiry()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var provider = DataProtectionProvider.Create(directory, p => p.SetApplicationName("Morita.LP.Razor"));
            var firstContext = new DefaultHttpContext();
            var first = Store(provider, firstContext, Now);
            Assert.True(first.Write(new string('t', 32), Now.AddDays(1)));
            var cookie = firstContext.Response.Headers.SetCookie.ToString().Split(';', 2)[0].Split('=', 2)[1];
            Assert.Contains("httponly", firstContext.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", firstContext.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);

            var restartedContext = new DefaultHttpContext();
            restartedContext.Request.Headers.Cookie = $"{CustomerAccountCookieStore.CookieName}={cookie}";
            Assert.Equal(new string('t', 32), Store(DataProtectionProvider.Create(directory, p => p.SetApplicationName("Morita.LP.Razor")), restartedContext, Now).Read()!.Token);

            var tampered = new DefaultHttpContext();
            tampered.Request.Headers.Cookie = $"{CustomerAccountCookieStore.CookieName}={cookie}x";
            Assert.Null(Store(provider, tampered, Now).Read());
            var expired = new DefaultHttpContext();
            expired.Request.Headers.Cookie = $"{CustomerAccountCookieStore.CookieName}={cookie}";
            Assert.Null(Store(provider, expired, Now.AddDays(2)).Read());
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task Account_client_forwards_session_and_maps_conflict_rate_limit_and_clears_on_401()
    {
        var cookieContext = new DefaultHttpContext();
        var cookie = Store(DataProtectionProvider.Create(Directory.CreateTempSubdirectory()), cookieContext, Now);
        cookie.Write(new string('s', 32), Now.AddDays(1));
        var handler = new StatusHandler(HttpStatusCode.Conflict);
        var client = CreateClient(handler, cookie);
        var conflict = await client.ClaimOrderAsync(new string('s', 32), "MF-0123456789ABCDEF", new string('o', 32));
        Assert.Equal(AccountLoadState.Conflict, conflict.State);
        Assert.Equal(new string('s', 32), handler.Request!.Headers.GetValues("X-Storefront-Session").Single());
        Assert.Equal("proxy-secret", handler.Request.Headers.GetValues("X-Morita-Proxy-Secret").Single());
        Assert.Equal("unknown", handler.Request.Headers.GetValues("X-Morita-Client-IP").Single());

        handler.Status = (HttpStatusCode)429;
        Assert.Equal(AccountLoadState.RateLimited, (await client.GetProfileAsync(new string('s', 32))).State);
        handler.Status = HttpStatusCode.Unauthorized;
        Assert.Equal(AccountLoadState.Unauthorized, (await client.GetProfileAsync(new string('s', 32))).State);
        Assert.Contains(CustomerAccountCookieStore.CookieName, cookieContext.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Verify_code_uses_current_time_for_session_issue_time_and_rejects_malformed_success()
    {
        var context = new DefaultHttpContext();
        var cookie = Store(DataProtectionProvider.Create(Directory.CreateTempSubdirectory()), context, Now);
        var handler = new StatusHandler(HttpStatusCode.OK, new { sessionToken = new string('s', 32), expiresAt = Now.AddDays(1), profile = new { accountId = Guid.NewGuid(), email = "a@example.com" } });
        var result = await CreateClient(handler, cookie).VerifyCodeAsync(Guid.NewGuid(), "123456", true, "customer-account-v1");
        Assert.Equal(Now, result.Value.Session.IssuedAt);
        Assert.Equal(Now.AddDays(1), result.Value.Session.ExpiresAt);
        handler.Body = "null";
        Assert.Equal(AccountLoadState.Malformed, (await CreateClient(handler, cookie).RequestCodeAsync("a@example.com")).State);
    }

    [Fact]
    public async Task Account_page_keeps_signed_in_view_when_closure_confirmation_is_missing()
    {
        var client = new AccountStub();
        var cookies = new SessionCookieStub();
        var page = new AccountModel(client, cookies) { PageContext = PageContext() };

        await page.OnPostRequestClosureCodeAsync(CancellationToken.None);

        Assert.True(page.SignedIn);
        Assert.False(page.ModelState.IsValid);
        Assert.Equal(0, client.ClosureRequests);
    }

    [Fact]
    public async Task Account_code_form_is_staged_and_uses_api_policy_version()
    {
        var challenge = new AccountCodeChallenge(Guid.NewGuid(), Now.AddMinutes(10), "privacy-v3");
        var client = new AccountStub { CodeResult = new(AccountLoadState.Success, challenge) };
        var page = new AccountModel(client, new SessionCookieStub()) { PageContext = PageContext(), EmailForm = new() { Email = "a@example.com" } };

        Assert.False(page.ChallengeIssued);
        await page.OnPostRequestCodeAsync(CancellationToken.None);

        Assert.True(page.ChallengeIssued);
        Assert.Equal("privacy-v3", page.PrivacyPolicyVersion);
        Assert.Equal(challenge.ExpiresAt, page.ChallengeExpiresAt);
        page.Verification = new() { ChallengeId = challenge.ChallengeId, Code = "123456" };
        await page.OnPostVerifyCodeAsync(CancellationToken.None);
        Assert.Contains(page.ModelState.Values.SelectMany(x => x.Errors), e => e.ErrorMessage!.Contains("Aceite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Partial_default_address_is_rejected_without_calling_the_api()
    {
        var client = new AccountStub();
        var page = new AccountModel(client, new SessionCookieStub())
        {
            PageContext = PageContext(),
            ProfileForm = new() { Address = new() { Street = "Rua A" } }
        };

        await page.OnPostSaveProfileAsync(CancellationToken.None);

        Assert.False(page.ModelState.IsValid);
        Assert.Equal(0, client.ProfileUpdates);
    }

    [Fact]
    public async Task Account_history_failure_is_not_rendered_as_empty_history()
    {
        var client = new AccountStub { OrdersResult = AccountResult<IReadOnlyList<PublicOrder>>.Failure(AccountLoadState.Unavailable) };
        var page = new AccountModel(client, new SessionCookieStub()) { PageContext = PageContext() };

        await page.OnGetAsync(CancellationToken.None);

        Assert.True(page.SignedIn);
        Assert.Empty(page.Orders);
        Assert.NotNull(page.OrdersError);
        Assert.True(page.OrdersLoaded);
    }

    [Fact]
    public async Task Unauthorized_account_history_clears_the_session_and_returns_to_sign_in()
    {
        var client = new AccountStub { OrdersResult = AccountResult<IReadOnlyList<PublicOrder>>.Failure(AccountLoadState.Unauthorized, "expired") };
        var cookies = new SessionCookieStub();
        var page = new AccountModel(client, cookies) { PageContext = PageContext() };

        await page.OnGetAsync(CancellationToken.None);

        Assert.False(page.SignedIn);
        Assert.Equal("expired", page.Error);
        Assert.Equal(1, cookies.ClearCalls);
    }

    [Fact]
    public async Task Unauthorized_account_order_detail_clears_the_session_and_returns_to_sign_in()
    {
        var client = new AccountStub { OrderResult = AccountResult<PublicOrder>.Failure(AccountLoadState.Unauthorized, "expired") };
        var cookies = new SessionCookieStub();
        var page = new AccountModel(client, cookies) { PageContext = PageContext(), PublicOrderNumber = "MF-0123456789ABCDEF" };

        await page.OnGetAsync(CancellationToken.None);

        Assert.False(page.SignedIn);
        Assert.Equal("expired", page.Error);
        Assert.Equal(1, cookies.ClearCalls);
    }

    [Fact]
    public async Task Disabled_customer_accounts_return_not_found_without_loading_profile()
    {
        var client = new AccountStub();
        var page = new AccountModel(
            client,
            new SessionCookieStub(),
            Options.Create(new StorefrontOptions { CustomerAccountsEnabled = false }))
        {
            PageContext = PageContext()
        };

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(0, client.ProfileReads);
    }

    [Fact]
    public async Task Email_change_keeps_current_session_active()
    {
        var client = new AccountStub();
        var cookies = new SessionCookieStub();
        var page = new AccountModel(client, cookies)
        {
            PageContext = PageContext(),
            Verification = new() { ChallengeId = Guid.NewGuid(), Code = "123456" }
        };

        await page.OnPostVerifyEmailCodeAsync(CancellationToken.None);

        Assert.Equal("E-mail atualizado. As outras sessões foram encerradas.", page.Message);
        Assert.Equal(0, cookies.ClearCalls);
        Assert.True(page.SignedIn);
    }

    [Fact]
    public async Task Manual_order_claim_preserves_conflict_and_clears_expired_session()
    {
        const string number = "MF-0123456789ABCDEF";
        var client = new AccountStub { ClaimResult = AccountResult<bool>.Failure(AccountLoadState.Conflict) };
        var cookies = new SessionCookieStub();
        var page = new OrderModel(new OrderStub(number), new OrderAccessStub(number), client, cookies)
        {
            PageContext = PageContext(),
            PublicOrderNumber = number
        };

        await page.OnPostClaimAsync(CancellationToken.None);
        Assert.Equal("Este pedido já pertence a outra conta.", page.ClaimMessage);

        client.ClaimResult = AccountResult<bool>.Failure(AccountLoadState.Unauthorized);
        await page.OnPostClaimAsync(CancellationToken.None);
        Assert.Equal(1, cookies.ClearCalls);
    }

    private static CustomerAccountCookieStore Store(IDataProtectionProvider provider, HttpContext context, DateTimeOffset now) => new(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), new FixedTime(now));
    private static CustomerAccountClient CreateClient(HttpMessageHandler handler, ICustomerAccountCookieStore cookie) => new(
        new HttpClient(handler) { BaseAddress = new("https://api.test/") },
        Options.Create(new CatalogApiOptions { BaseUrl = "https://api.test", TimeoutSeconds = 2, ProxySecret = "proxy-secret" }),
        NullLogger<CustomerAccountClient>.Instance,
        cookie,
        new FixedTime(Now),
        new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
        new TestEnvironment());

    private static PageContext PageContext()
    {
        var services = new ServiceCollection().AddLogging().AddMvcCore().AddDataAnnotations().Services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var page = new PageContext(new ActionContext(context, new RouteData(), new PageActionDescriptor()));
        page.ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());
        return page;
    }

    private sealed class StatusHandler(HttpStatusCode status, object? body = null) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = status;
        public string Body { get; set; } = body is null ? "" : System.Text.Json.JsonSerializer.Serialize(body);
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Request = request; return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, System.Text.Encoding.UTF8, "application/json") }); }
    }
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class SessionCookieStub : ICustomerAccountCookieStore
    {
        public int ClearCalls { get; private set; }
        public CustomerAccountSession? Read() => new(new string('s', 32), Now, Now.AddDays(1));
        public bool Write(string token, DateTimeOffset expiresAt) => true;
        public void Clear() => ClearCalls++;
    }
    private sealed class AccountStub : ICustomerAccountClient
    {
        public int ClosureRequests { get; private set; }
        public int ProfileUpdates { get; private set; }
        public int ProfileReads { get; private set; }
        public AccountResult<bool> ClaimResult { get; set; } = new(AccountLoadState.Success, true);
        public AccountResult<AccountCodeChallenge> CodeResult { get; set; } = AccountResult<AccountCodeChallenge>.Failure(AccountLoadState.Unavailable);
        public AccountResult<IReadOnlyList<PublicOrder>> OrdersResult { get; set; } = new(AccountLoadState.Success, []);
        public AccountResult<PublicOrder> OrderResult { get; set; } = AccountResult<PublicOrder>.Failure(AccountLoadState.NotFound);
        public Task<AccountResult<AccountCodeChallenge>> RequestCodeAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(CodeResult);
        public Task<AccountResult<(CustomerAccountSession Session, CustomerAccountProfile Profile)>> VerifyCodeAsync(Guid challengeId, string code, bool acceptedPrivacyPolicy, string privacyPolicyVersion, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<(CustomerAccountSession, CustomerAccountProfile)>.Failure(AccountLoadState.Unavailable));
        public Task<AccountResult<CustomerAccountProfile>> GetProfileAsync(string token, CancellationToken cancellationToken = default) { ProfileReads++; return Task.FromResult(new AccountResult<CustomerAccountProfile>(AccountLoadState.Success, new() { Email = "customer@example.com" })); }
        public Task<AccountResult<bool>> UpdateProfileAsync(string token, string? name, string? phone, CustomerAccountAddress? address, CancellationToken cancellationToken = default) { ProfileUpdates++; return Task.FromResult(new AccountResult<bool>(AccountLoadState.Success, true)); }
        public Task<AccountResult<AccountCodeChallenge>> RequestEmailCodeAsync(string token, string email, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<AccountCodeChallenge>.Failure(AccountLoadState.Unavailable));
        public Task<AccountResult<bool>> VerifyEmailCodeAsync(string token, Guid challengeId, string code, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<bool>(AccountLoadState.Success, true));
        public Task<AccountResult<AccountCodeChallenge>> RequestClosureCodeAsync(string token, CancellationToken cancellationToken = default) { ClosureRequests++; return Task.FromResult(new AccountResult<AccountCodeChallenge>(AccountLoadState.Success, new(Guid.NewGuid(), Now.AddMinutes(10)))); }
        public Task<AccountResult<bool>> VerifyClosureCodeAsync(string token, Guid challengeId, string code, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<bool>(AccountLoadState.Success, true));
        public Task<AccountResult<bool>> LogoutAsync(string token, bool all, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<bool>(AccountLoadState.Success, true));
        public Task<AccountResult<IReadOnlyList<PublicOrder>>> GetOrdersAsync(string token, CancellationToken cancellationToken = default) => Task.FromResult(OrdersResult);
        public Task<AccountResult<PublicOrder>> GetOrderAsync(string token, string number, CancellationToken cancellationToken = default) => Task.FromResult(OrderResult);
        public Task<AccountResult<bool>> ClaimOrderAsync(string token, string number, string accessToken, CancellationToken cancellationToken = default) => Task.FromResult(ClaimResult);
    }
    private sealed class OrderStub(string number) : IOrderClient
    {
        public Task<OrderResult> GetAsync(string publicOrderNumber, string token, CancellationToken cancellationToken = default) => Task.FromResult(new OrderResult(OrderLoadState.Success, new PublicOrder { PublicOrderNumber = number }));
    }
    private sealed class OrderAccessStub(string number) : IOrderAccessCookieStore
    {
        public OrderAccess? Read(string publicOrderNumber) => new(number, new string('o', 32), Now, Now.AddDays(1));
        public bool Write(string publicOrderNumber, string token) => true;
        public void Clear() { }
    }
    private sealed class TestEnvironment : IHostEnvironment { public string EnvironmentName { get; set; } = Environments.Development; public string ApplicationName { get; set; } = "tests"; public string ApplicationVersion { get; set; } = "tests"; public string ContentRootPath { get; set; } = AppContext.BaseDirectory; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }
}
