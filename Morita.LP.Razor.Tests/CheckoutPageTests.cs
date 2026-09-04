using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Pages;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class CheckoutPageTests
{
    [Fact]
    public async Task Ambiguous_retries_reuse_the_same_draft_credentials()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var draft = new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System);
        var page = CreatePage(context, cart, api, draft, offer);

        await page.OnGetAsync(CancellationToken.None);
        var cookie = context.Response.Headers.SetCookie.ToString().Split(';', 2)[0];
        context.Request.Headers.Cookie = cookie;
        context.Response.Headers.Remove("Set-Cookie");
        page.Contact.Name = "Ana Teste";
        page.Contact.Email = "ana@example.com";
        page.Contact.Phone = "15999999999";
        await page.OnPostAsync(CancellationToken.None);
        context.Request.Headers.Cookie = cookie;
        context.Response.Headers.Remove("Set-Cookie");
        await page.OnPostAsync(CancellationToken.None);

        Assert.Equal(2, api.Credentials.Count);
        Assert.Equal(api.Credentials[0], api.Credentials[1]);
    }

    [Fact]
    public async Task Shipping_quote_and_checkout_preserve_authoritative_quote_boundary()
    {
        var offer = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout
        {
            Configuration = new(CheckoutLoadState.Success, new() { PickupEnabled = false, ShippingEnabled = true, Currency = "BRL" }),
            ShippingQuote = new(CheckoutLoadState.Success, new ShippingQuote { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10), Options = [new() { PublicShippingQuoteId = quoteId, ServiceName = "PAC", CarrierName = "Correios", Price = 18, MinimumDeliveryDays = 4, MaximumDeliveryDays = 7 }] })
        };
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer);
        api.Configuration = new(CheckoutLoadState.Success, new() { PickupEnabled = false, ShippingEnabled = true, Currency = "BRL" });
        page.ShippingAddress.PostalCode = "01310-100";

        await page.OnPostQuoteShippingAsync(CancellationToken.None);

        Assert.Equal("01310-100", api.LastShippingQuoteRequest!.DestinationPostalCode);
        Assert.Equal(quoteId, Assert.Single(page.ShippingQuotes.Quote!.Options).PublicShippingQuoteId);

        page.PublicShippingQuoteId = quoteId;
        page.ShippingAddress = new() { Recipient = "Ana", Street = "Avenida Paulista", Number = "1000", Neighborhood = "Bela Vista", City = "São Paulo", State = "SP", PostalCode = "01310-100" };
        page.Contact = new() { Name = "Ana Teste", Email = "ana@example.com", Phone = "11999999999" };
        await page.OnPostAsync(CancellationToken.None);

        var fulfillment = Assert.Single(api.Requests).Fulfillment;
        Assert.Equal("shipping", fulfillment.Method);
        Assert.Equal(quoteId, fulfillment.PublicShippingQuoteId);
        Assert.Equal("01310-100", fulfillment.ShippingAddress!.PostalCode);
    }

    [Fact]
    public async Task Signed_in_checkout_locks_email_forwards_session_and_preserves_pickup_address_on_save()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout { CreateResults = new Queue<CheckoutResult>([SuccessfulCheckout()]) };
        var account = new RecordingAccount();
        var accountCookies = new RecordingAccountCookie();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer, account, accountCookies);

        await page.OnGetAsync(CancellationToken.None);
        Assert.True(page.AccountPrefilled);
        Assert.Equal("customer@example.com", page.Contact.Email);

        page.Contact.Email = "tampered@example.com";
        page.SaveAccountDetails = true;
        await page.OnPostAsync(CancellationToken.None);

        Assert.Equal("customer@example.com", Assert.Single(api.Requests).Contact.Email);
        Assert.Equal(accountCookies.Session!.Token, Assert.Single(api.AccountSessions));
        Assert.Equal("Saved street", account.SavedAddress!.Street);
    }

    [Fact]
    public async Task Expired_account_requires_explicit_guest_continuation()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout
        {
            CreateResults = new Queue<CheckoutResult>([
                CheckoutResult.Failure(CheckoutLoadState.Unauthorized),
                SuccessfulCheckout()
            ])
        };
        var accountCookies = new RecordingAccountCookie();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer, new RecordingAccount(), accountCookies);
        page.Contact = new() { Name = "Customer", Email = "customer@example.com", Phone = "15999999999" };

        await page.OnPostAsync(CancellationToken.None);
        Assert.Single(api.AccountSessions);
        Assert.True(page.AccountNeedsGuestConfirmation);
        Assert.Contains("histórico", page.AccountMessage!, StringComparison.OrdinalIgnoreCase);

        page.ContinueAsGuest = true;
        await page.OnPostAsync(CancellationToken.None);

        Assert.Equal(2, api.AccountSessions.Count);
        Assert.NotNull(api.AccountSessions[0]);
        Assert.Null(api.AccountSessions[1]);
        Assert.Equal(1, accountCookies.ClearCalls);
    }

    [Fact]
    public async Task Transient_account_failure_preserves_login_and_requires_explicit_guest_continuation()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout { CreateResults = new Queue<CheckoutResult>([SuccessfulCheckout()]) };
        var account = new RecordingAccount { ProfileResult = AccountResult<CustomerAccountProfile>.Failure(AccountLoadState.Unavailable) };
        var accountCookies = new RecordingAccountCookie();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer, account, accountCookies);
        page.Contact = new() { Name = "Customer", Email = "customer@example.com", Phone = "15999999999" };

        await page.OnPostAsync(CancellationToken.None);

        Assert.True(page.AccountNeedsGuestConfirmation);
        Assert.Empty(api.Requests);
        Assert.Equal(0, accountCookies.ClearCalls);

        page.ContinueAsGuest = true;
        await page.OnPostAsync(CancellationToken.None);

        Assert.Null(Assert.Single(api.AccountSessions));
        Assert.Equal(0, accountCookies.ClearCalls);
    }

    [Fact]
    public async Task Disabled_accounts_ignore_and_clear_stale_session_during_checkout()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout { CreateResults = new Queue<CheckoutResult>([SuccessfulCheckout()]) };
        var account = new RecordingAccount();
        var accountCookies = new RecordingAccountCookie();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(
            context,
            cart,
            api,
            new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System),
            offer,
            account,
            accountCookies,
            Options.Create(new StorefrontOptions { CustomerAccountsEnabled = false }));
        page.Contact = new() { Name = "Guest", Email = "guest@example.com", Phone = "15999999999" };

        await page.OnPostAsync(CancellationToken.None);

        Assert.Equal(0, account.ProfileReads);
        Assert.Null(Assert.Single(api.AccountSessions));
        Assert.Equal(1, accountCookies.ClearCalls);
    }

    private static CheckoutModel CreatePage(DefaultHttpContext context, TestCart cart, RecordingCheckout api, ICheckoutDraftCookieStore draft, Guid offer, ICustomerAccountClient? account = null, ICustomerAccountCookieStore? accountCookies = null, IOptions<StorefrontOptions>? storefrontOptions = null)
    {
        var config = new CheckoutConfigurationResult(CheckoutLoadState.Success, new() { PickupEnabled = true, PublicPickupId = Guid.NewGuid(), Currency = "BRL", Pickup = new() { PublicPickupId = Guid.NewGuid(), DisplayName = "Loja", Address = new() { Street = "Rua", Number = "1", Neighborhood = "Centro", City = "Sorocaba", State = "SP", PostalCode = "18000-000" } } });
        var quote = CatalogQuoteResult.Success("BRL", 10, [new CatalogQuoteLine { PublicOfferId = offer, Quantity = 1, Availability = "available", Presentation = "Kimono", Currency = "BRL", UnitPrice = 10, LinePrice = 10 }]);
        var page = new CheckoutModel(cart, new StubCatalog(quote), api, draft, new NoopAccess(), new CheckoutRateLimiter(TimeProvider.System), account ?? new NoopAccount(), accountCookies ?? new NoopAccountCookie(), storefrontOptions)
        {
            Contact = new CheckoutModel.ContactInput()
        };
        api.Configuration = config;
        page.PageContext = new PageContext(new Microsoft.AspNetCore.Mvc.ActionContext(context, new RouteData(), new PageActionDescriptor()));
        page.PageContext.ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());
        return page;
    }

    private static CheckoutResult SuccessfulCheckout() => new(CheckoutLoadState.Success, new CheckoutResponse
    {
        PublicCheckoutId = Guid.NewGuid(),
        Status = "active",
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
        AccessExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        Currency = "BRL"
    });

    private static IServiceProvider Services() => new ServiceCollection().AddSingleton<IHostEnvironment>(new TestEnvironment()).BuildServiceProvider();

    private sealed class RecordingCheckout : ICheckoutClient
    {
        public CheckoutConfigurationResult Configuration { get; set; } = CheckoutConfigurationResult.Failure(CheckoutLoadState.Unavailable);
        public ShippingQuoteResult ShippingQuote { get; set; } = ShippingQuoteResult.Failure(CheckoutLoadState.Unavailable);
        public ShippingQuoteRequest? LastShippingQuoteRequest { get; private set; }
        public List<(string Key, string Token)> Credentials { get; } = [];
        public List<CheckoutCreateRequest> Requests { get; } = [];
        public List<string?> AccountSessions { get; } = [];
        public Queue<CheckoutResult>? CreateResults { get; set; }
        public Task<CheckoutConfigurationResult> GetConfigurationAsync(CancellationToken cancellationToken = default) => Task.FromResult(Configuration);
        public Task<ShippingQuoteResult> QuoteShippingAsync(ShippingQuoteRequest request, CancellationToken cancellationToken = default) { LastShippingQuoteRequest = request; return Task.FromResult(ShippingQuote); }
        public Task<CheckoutResult> CreateAsync(CheckoutCreateRequest request, string idempotencyKey, string accessToken, CancellationToken cancellationToken = default) { Requests.Add(request); Credentials.Add((idempotencyKey, accessToken)); return Task.FromResult(CheckoutResult.Failure(CheckoutLoadState.Timeout, "timeout")); }
        public Task<CheckoutResult> CreateForAccountAsync(CheckoutCreateRequest request, string idempotencyKey, string accessToken, string? storefrontSession, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Credentials.Add((idempotencyKey, accessToken));
            AccountSessions.Add(storefrontSession);
            return Task.FromResult(CreateResults is { Count: > 0 } ? CreateResults.Dequeue() : CheckoutResult.Failure(CheckoutLoadState.Timeout, "timeout"));
        }
        public Task<CheckoutResult> GetAsync(Guid publicCheckoutId, string accessToken, CancellationToken cancellationToken = default) => Task.FromResult(CheckoutResult.Failure(CheckoutLoadState.NotFound));
        public Task<CheckoutResult> CancelAsync(Guid publicCheckoutId, string accessToken, CancellationToken cancellationToken = default) => Task.FromResult(CheckoutResult.Failure(CheckoutLoadState.NotFound));
    }

    private sealed class StubCatalog(CatalogQuoteResult quote) : ICatalogClient
    {
        public Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default) => Task.FromResult(CatalogResult.Empty());
        public Task<CatalogQuoteResult> QuoteAsync(CatalogQuoteRequest request, CancellationToken cancellationToken = default) => Task.FromResult(quote);
    }

    private sealed class TestCart(CartState state) : ICartCookieStore
    {
        public CartState Read() => state;
        public bool Add(Guid offerId, int quantity) => true;
        public bool Update(Guid offerId, int quantity) => true;
        public bool Remove(Guid offerId) => true;
        public void Clear() { }
    }

    private sealed class NoopAccess : ICheckoutAccessCookieStore
    {
        public CheckoutAccess? Read(Guid publicCheckoutId) => null;
        public bool Write(CheckoutResponse checkout, string token) => true;
        public void Clear() { }
    }

    private sealed class NoopAccountCookie : ICustomerAccountCookieStore
    {
        public CustomerAccountSession? Read() => null;
        public bool Write(string token, DateTimeOffset expiresAt) => true;
        public void Clear() { }
    }

    private sealed class RecordingAccountCookie : ICustomerAccountCookieStore
    {
        public CustomerAccountSession? Session { get; private set; } = new(new string('s', 32), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        public int ClearCalls { get; private set; }
        public CustomerAccountSession? Read() => Session;
        public bool Write(string token, DateTimeOffset expiresAt) => true;
        public void Clear() { ClearCalls++; Session = null; }
    }

    private sealed class RecordingAccount : NoopAccount
    {
        private readonly CustomerAccountAddress address = new() { Recipient = "Customer", Street = "Saved street", Number = "10", Neighborhood = "Centro", City = "Sorocaba", State = "SP", PostalCode = "18000000" };
        public CustomerAccountAddress? SavedAddress { get; private set; }
        public int ProfileReads { get; private set; }
        public AccountResult<CustomerAccountProfile>? ProfileResult { get; set; }
        public override Task<AccountResult<CustomerAccountProfile>> GetProfileAsync(string token, CancellationToken cancellationToken = default) { ProfileReads++; return Task.FromResult(ProfileResult ?? new AccountResult<CustomerAccountProfile>(AccountLoadState.Success, new() { Email = "customer@example.com", Name = "Customer", Phone = "15999999999", Address = address })); }
        public override Task<AccountResult<bool>> UpdateProfileAsync(string token, string? name, string? phone, CustomerAccountAddress? savedAddress, CancellationToken cancellationToken = default) { SavedAddress = savedAddress; return Task.FromResult(new AccountResult<bool>(AccountLoadState.Success, true)); }
    }

    private class NoopAccount : ICustomerAccountClient
    {
        public Task<AccountResult<AccountCodeChallenge>> RequestCodeAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<AccountCodeChallenge>.Failure(AccountLoadState.Unavailable));
        public Task<AccountResult<(CustomerAccountSession Session, CustomerAccountProfile Profile)>> VerifyCodeAsync(Guid challengeId, string code, bool acceptedPrivacyPolicy, string privacyPolicyVersion, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<(CustomerAccountSession, CustomerAccountProfile)>.Failure(AccountLoadState.Unavailable));
        public virtual Task<AccountResult<CustomerAccountProfile>> GetProfileAsync(string token, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<CustomerAccountProfile>.Failure(AccountLoadState.Unauthorized));
        public virtual Task<AccountResult<bool>> UpdateProfileAsync(string token, string? name, string? phone, CustomerAccountAddress? address, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<bool>(AccountLoadState.Success, true));
        public Task<AccountResult<AccountCodeChallenge>> RequestEmailCodeAsync(string token, string email, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<AccountCodeChallenge>.Failure(AccountLoadState.Unavailable));
        public Task<AccountResult<bool>> VerifyEmailCodeAsync(string token, Guid challengeId, string code, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<bool>(AccountLoadState.Success, true));
        public Task<AccountResult<AccountCodeChallenge>> RequestClosureCodeAsync(string token, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<AccountCodeChallenge>.Failure(AccountLoadState.Unavailable));
        public Task<AccountResult<bool>> VerifyClosureCodeAsync(string token, Guid challengeId, string code, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<bool>(AccountLoadState.Success, true));
        public Task<AccountResult<bool>> LogoutAsync(string token, bool all, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<bool>(AccountLoadState.Success, true));
        public Task<AccountResult<IReadOnlyList<PublicOrder>>> GetOrdersAsync(string token, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<IReadOnlyList<PublicOrder>>(AccountLoadState.Success, []));
        public Task<AccountResult<PublicOrder>> GetOrderAsync(string token, string number, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<PublicOrder>.Failure(AccountLoadState.NotFound));
        public Task<AccountResult<bool>> ClaimOrderAsync(string token, string number, string accessToken, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<bool>(AccountLoadState.Success, true));
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ApplicationVersion { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
