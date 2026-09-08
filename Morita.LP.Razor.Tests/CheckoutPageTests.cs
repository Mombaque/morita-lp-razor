using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;
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
    }

    [Fact]
    public async Task Missing_account_session_redirects_to_sign_in_instead_of_guest_checkout()
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

        var result = await page.OnPostAsync(CancellationToken.None);
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Single(api.AccountSessions);
    }

    [Fact]
    public async Task Transient_account_failure_blocks_checkout_without_guest_fallback()
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

         Assert.IsType<PageResult>(await page.OnPostAsync(CancellationToken.None));
        Assert.Empty(api.Requests);
        Assert.Equal(0, accountCookies.ClearCalls);
    }

    [Fact]
    public async Task Checkout_get_keeps_cookie_and_renders_retry_for_transient_account_failure()
    {
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(Guid.NewGuid(), 1)]));
        var account = new RecordingAccount { ProfileResult = AccountResult<CustomerAccountProfile>.Failure(AccountLoadState.Timeout, "timeout") };
        var cookies = new RecordingAccountCookie();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, new RecordingCheckout(), new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), Guid.NewGuid(), account, cookies);

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(0, cookies.ClearCalls);
        Assert.Contains("timeout", page.AccountMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Checkout_get_clears_unauthorized_cookie_and_redirects_to_sign_in()
    {
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(Guid.NewGuid(), 1)]));
        var account = new RecordingAccount { ProfileResult = AccountResult<CustomerAccountProfile>.Failure(AccountLoadState.Unauthorized) };
        var cookies = new RecordingAccountCookie();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, new RecordingCheckout(), new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), Guid.NewGuid(), account, cookies);

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, cookies.ClearCalls);
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
        Assert.Empty(api.AccountSessions);
        Assert.Equal(1, accountCookies.ClearCalls);
    }

    [Fact]
    public async Task Selected_saved_address_overrides_tampered_posted_address_fields()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout { CreateResults = new Queue<CheckoutResult>([SuccessfulCheckout()]) };
        var account = new RecordingAccount();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer, account, new RecordingAccountCookie());
        api.Configuration = new(CheckoutLoadState.Success, new() { PickupEnabled = false, ShippingEnabled = true, Currency = "BRL" });
        await page.OnGetAsync(CancellationToken.None);
        page.FulfillmentMethod = "shipping";
        page.SelectedAddressId = account.SavedAddressId;
        page.PublicShippingQuoteId = Guid.NewGuid();
        page.ShippingAddress = new() { Recipient = "Tampered", Street = "Fake street", Number = "999", Neighborhood = "Fake", City = "Fake", State = "RJ", PostalCode = "01000-000" };
        page.Contact = new() { Name = "Customer", Email = "customer@example.com", Phone = "15999999999" };

        await page.OnPostAsync(CancellationToken.None);

        Assert.True(api.Requests.Count == 1, $"{page.ErrorMessage}; model state: {string.Join(" | ", page.ModelState.Values.SelectMany(value => value.Errors).Select(error => error.ErrorMessage))}");
        var address = api.Requests[0].Fulfillment.ShippingAddress;
        Assert.Equal("Saved street", address!.Street);
        Assert.Equal("SP", address.State);
    }

    [Fact]
    public async Task Unknown_saved_address_does_not_quote_with_posted_address_fields()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout();
        var account = new RecordingAccount();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer, account, new RecordingAccountCookie());
        api.Configuration = new(CheckoutLoadState.Success, new() { PickupEnabled = false, ShippingEnabled = true, Currency = "BRL" });
        page.SelectedAddressId = Guid.NewGuid();
        page.ShippingAddress = new() { Recipient = "Tampered", Street = "Fake street", Number = "999", Neighborhood = "Fake", City = "Fake", State = "RJ", PostalCode = "01000000" };

        var result = await page.OnPostQuoteShippingAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Null(api.LastShippingQuoteRequest);
        Assert.False(page.ModelState.IsValid);
    }

    [Fact]
    public async Task New_saved_address_failure_is_reported_after_successful_reservation()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout { CreateResults = new Queue<CheckoutResult>([SuccessfulCheckout()]) };
        var account = new RecordingAccount();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer, account, new RecordingAccountCookie());
        page.TempData = new TempDataDictionary(context, new TestTempDataProvider());
        api.Configuration = new(CheckoutLoadState.Success, new() { PickupEnabled = false, ShippingEnabled = true, Currency = "BRL" });
        await page.OnGetAsync(CancellationToken.None);
        page.FulfillmentMethod = "shipping";
        page.SelectedAddressId = null;
        page.PublicShippingQuoteId = Guid.NewGuid();
        page.SaveShippingAddress = true;
        page.ShippingAddress = new() { Recipient = "Customer", Street = "Saved street", Number = "10", Neighborhood = "Centro", City = "Sorocaba", State = "SP", PostalCode = "18000000" };
        page.Contact = new() { Name = "Customer", Email = "customer@example.com", Phone = "15999999999" };

        await page.OnPostAsync(CancellationToken.None);

        Assert.Contains("Não foi possível salvar o novo endereço", page.TempData["CheckoutAccountMessage"]?.ToString());
        Assert.Equal(1, account.CreateAddressCalls);
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

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

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
        public CustomerAccountSession? Read() => new(new string('s', 32), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
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
        private readonly CustomerAccountAddress address = new() { PublicAddressId = Guid.NewGuid(), Recipient = "Customer", Street = "Saved street", Number = "10", Neighborhood = "Centro", City = "Sorocaba", State = "SP", PostalCode = "18000000", IsDefault = true };
        public Guid SavedAddressId => address.PublicAddressId;
        public int ProfileReads { get; private set; }
        public AccountResult<CustomerAccountProfile>? ProfileResult { get; set; }
        public AccountResult<CustomerAccountAddress> CreateAddressResult { get; set; } = AccountResult<CustomerAccountAddress>.Failure(AccountLoadState.Unavailable, "address unavailable");
        public AccountResult<bool> SetDefaultAddressResult { get; set; } = AccountResult<bool>.Failure(AccountLoadState.Unavailable, "default unavailable");
        public int CreateAddressCalls { get; private set; }
        public override Task<AccountResult<CustomerAccountProfile>> GetProfileAsync(string token, CancellationToken cancellationToken = default) { ProfileReads++; return Task.FromResult(ProfileResult ?? new AccountResult<CustomerAccountProfile>(AccountLoadState.Success, new() { Email = "customer@example.com", Name = "Customer", Phone = "15999999999" })); }
        public override Task<AccountResult<IReadOnlyList<CustomerAccountAddress>>> GetAddressesAsync(string token, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<IReadOnlyList<CustomerAccountAddress>>(AccountLoadState.Success, [address]));
        public override Task<AccountResult<CustomerAccountAddress>> CreateAddressAsync(string token, CustomerAccountAddress savedAddress, CancellationToken cancellationToken = default) { CreateAddressCalls++; return Task.FromResult(CreateAddressResult); }
        public override Task<AccountResult<bool>> SetDefaultAddressAsync(string token, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(SetDefaultAddressResult);
    }

    private class NoopAccount : ICustomerAccountClient
    {
        public Task<AccountResult<AccountCodeChallenge>> RequestCodeAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<AccountCodeChallenge>.Failure(AccountLoadState.Unavailable));
        public Task<AccountResult<(CustomerAccountSession Session, CustomerAccountProfile Profile)>> VerifyCodeAsync(Guid challengeId, string code, bool acceptedPrivacyPolicy, string privacyPolicyVersion, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<(CustomerAccountSession, CustomerAccountProfile)>.Failure(AccountLoadState.Unavailable));
        public virtual Task<AccountResult<CustomerAccountProfile>> GetProfileAsync(string token, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<CustomerAccountProfile>(AccountLoadState.Success, new() { Email = "customer@example.com" }));
        public virtual Task<AccountResult<bool>> UpdateProfileAsync(string token, string? name, string? phone, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<bool>(AccountLoadState.Success, true));
        public virtual Task<AccountResult<IReadOnlyList<CustomerAccountAddress>>> GetAddressesAsync(string token, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<IReadOnlyList<CustomerAccountAddress>>(AccountLoadState.Success, []));
        public virtual Task<AccountResult<CustomerAccountAddress>> CreateAddressAsync(string token, CustomerAccountAddress address, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<CustomerAccountAddress>.Failure(AccountLoadState.Unavailable));
        public virtual Task<AccountResult<CustomerAccountAddress>> UpdateAddressAsync(string token, Guid id, CustomerAccountAddress address, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<CustomerAccountAddress>.Failure(AccountLoadState.Unavailable));
        public virtual Task<AccountResult<bool>> DeleteAddressAsync(string token, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<bool>.Failure(AccountLoadState.Unavailable));
        public virtual Task<AccountResult<bool>> SetDefaultAddressAsync(string token, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<bool>.Failure(AccountLoadState.Unavailable));
        public virtual Task<AccountResult<StorefrontAccountOrderPage>> GetOrdersAsync(string token, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<StorefrontAccountOrderPage>(AccountLoadState.Success, new() { Page = page, PageSize = pageSize }));
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
