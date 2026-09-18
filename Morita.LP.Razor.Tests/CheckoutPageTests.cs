using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public void Saved_address_label_is_optional_for_pickup_checkout()
    {
        var property = typeof(CheckoutModel).GetProperty(nameof(CheckoutModel.SavedAddressLabel))!;

        var nullability = new NullabilityInfoContext().Create(property);

        Assert.Equal(NullabilityState.Nullable, nullability.WriteState);
    }

    [Fact]
    public void Shipping_address_complement_is_optional()
    {
        var property = typeof(CheckoutModel.ShippingAddressInput).GetProperty(nameof(CheckoutModel.ShippingAddressInput.Complement))!;
        var nullability = new NullabilityInfoContext().Create(property);

        Assert.Equal(NullabilityState.Nullable, nullability.WriteState);
    }

    [Fact]
    public async Task Shipping_checkout_explains_that_a_quote_is_required_before_submission()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer);
        api.Configuration = new(CheckoutLoadState.Success, new() { PickupEnabled = false, ShippingEnabled = true, Currency = "BRL" });

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(page.CanSubmit);
        Assert.Equal("Calcule o frete e escolha uma opção de entrega.", page.SubmitFeedback);
    }

    [Fact]
    public async Task Shipping_is_the_default_fulfillment_when_both_options_are_enabled()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer);
        api.Configuration = new(CheckoutLoadState.Success, new()
        {
            PickupEnabled = true,
            ShippingEnabled = true,
            PublicPickupId = Guid.NewGuid(),
            Currency = "BRL"
        });

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("shipping", page.FulfillmentMethod);
    }

    [Fact]
    public async Task Pickup_only_checkout_skips_address_loading()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout();
        var account = new RecordingAccount { AddressesResult = AccountResult<IReadOnlyList<CustomerAccountAddress>>.Failure(AccountLoadState.Unavailable, "address book unavailable") };
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer, account, new RecordingAccountCookie());
        api.Configuration = new(CheckoutLoadState.Success, new() { PickupEnabled = true, PublicPickupId = Guid.NewGuid(), ShippingEnabled = false, Currency = "BRL" });

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("pickup", page.FulfillmentMethod);
        Assert.True(page.CanSubmit);
        Assert.Equal(0, account.AddressReads);
    }

    [Fact]
    public async Task Shipping_quote_is_rejected_without_loading_addresses_when_shipping_turns_off()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout();
        var account = new RecordingAccount();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer, account, new RecordingAccountCookie());
        api.Configuration = new(CheckoutLoadState.Success, new() { PickupEnabled = true, PublicPickupId = Guid.NewGuid(), ShippingEnabled = false, Currency = "BRL" });
        page.SelectedAddressId = account.SavedAddressId;

        var result = await page.OnPostQuoteShippingAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("A entrega está temporariamente indisponível.", page.ErrorMessage);
        Assert.Null(api.LastShippingQuoteRequest);
        Assert.Equal(0, account.AddressReads);
    }

    [Fact]
    public async Task Stale_shipping_checkout_is_rejected_without_creating_a_reservation()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout();
        var account = new RecordingAccount();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer, account, new RecordingAccountCookie());
        api.Configuration = new(CheckoutLoadState.Success, new() { PickupEnabled = true, PublicPickupId = Guid.NewGuid(), ShippingEnabled = false, Currency = "BRL" });
        page.FulfillmentMethod = "shipping";
        page.SelectedAddressId = account.SavedAddressId;

        var result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("A entrega está temporariamente indisponível. Escolha retirada na loja.", page.ErrorMessage);
        Assert.Empty(api.Requests);
        Assert.Equal(0, account.AddressReads);
    }

    [Fact]
    public async Task Checkout_without_any_fulfillment_method_is_unavailable()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout();
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer);
        api.Configuration = new(CheckoutLoadState.Success, new() { PickupEnabled = false, ShippingEnabled = false, Currency = "BRL" });

        var result = await page.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(page.HasAvailableFulfillment);
        Assert.Equal("Nenhuma forma de entrega está disponível no momento.", page.ErrorMessage);
    }

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
    public async Task New_address_quote_preserves_posted_postal_code_when_default_exists()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout
        {
            Configuration = new(CheckoutLoadState.Success, new() { PickupEnabled = false, ShippingEnabled = true, Currency = "BRL" }),
            ShippingQuote = new(CheckoutLoadState.Success, new ShippingQuote
            {
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                Options = [new() { PublicShippingQuoteId = Guid.NewGuid(), ServiceName = "PAC", CarrierName = "Correios", Price = 18, MinimumDeliveryDays = 4, MaximumDeliveryDays = 7 }]
            })
        };
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer, new RecordingAccount(), new RecordingAccountCookie());
        api.Configuration = new(CheckoutLoadState.Success, new() { PickupEnabled = false, ShippingEnabled = true, Currency = "BRL" });
        page.SelectedAddressId = null;
        page.ShippingAddress.PostalCode = "18120000";

        var result = await page.OnPostQuoteShippingAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(api.LastShippingQuoteRequest is not null, $"{page.ErrorState}: {page.ErrorMessage}; postal={page.ShippingAddress?.PostalCode}; modelState={string.Join(" | ", page.ModelState.Values.SelectMany(value => value.Errors).Select(error => error.ErrorMessage))}");
        Assert.Equal("18120000", api.LastShippingQuoteRequest!.DestinationPostalCode);
    }

    [Fact]
    public async Task Shipping_quote_button_posts_to_quote_handler()
    {
        var offer = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout
        {
            Configuration = new(CheckoutLoadState.Success, new() { PickupEnabled = false, ShippingEnabled = true, Currency = "BRL" }),
            ShippingQuote = new(CheckoutLoadState.Success, new ShippingQuote
            {
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                Options = [new() { PublicShippingQuoteId = quoteId, ServiceName = "PAC", CarrierName = "Correios", Price = 18, MinimumDeliveryDays = 4, MaximumDeliveryDays = 7 }]
            })
        };
        var account = new RecordingAccount();
        var accountCookies = new RecordingAccountCookie();

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICartCookieStore>();
                services.RemoveAll<ICatalogClient>();
                services.RemoveAll<ICheckoutClient>();
                services.RemoveAll<ICustomerAccountClient>();
                services.RemoveAll<ICustomerAccountCookieStore>();
                services.AddScoped<ICartCookieStore>(_ => cart);
                services.AddScoped<ICatalogClient>(_ => new StubCatalog(CatalogQuoteResult.Success("BRL", 10, [new CatalogQuoteLine
                {
                    PublicOfferId = offer,
                    Quantity = 1,
                    Availability = "available",
                    Presentation = "Kimono",
                    Currency = "BRL",
                    UnitPrice = 10,
                    LinePrice = 10
                }])));
                services.AddScoped<ICheckoutClient>(_ => api);
                services.AddScoped<ICustomerAccountClient>(_ => account);
                services.AddScoped<ICustomerAccountCookieStore>(_ => accountCookies);
            });
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/checkout");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"__RequestVerificationToken\"", body);
        var quoteButton = System.Text.RegularExpressions.Regex.Match(body, "<button[^>]*>Calcular frete").Value;
        Assert.Equal("<button class=\"commerce-button commerce-button-secondary\" type=\"submit\" formnovalidate data-quote-shipping formaction=\"/checkout?handler=QuoteShipping\">Calcular frete", quoteButton);

        var token = System.Text.RegularExpressions.Regex.Match(body, "name=\\\"request-verification-token\\\" content=\\\"([^\\\"]+)").Groups[1].Value;
        var posted = await client.PostAsync("/checkout?handler=QuoteShipping", new FormUrlEncodedContent([
            new KeyValuePair<string, string>("FulfillmentMethod", "shipping"),
            new KeyValuePair<string, string>("ShippingAddress.PostalCode", "18000000"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        ]));

        Assert.Equal(System.Net.HttpStatusCode.OK, posted.StatusCode);
        Assert.Equal("18000000", api.LastShippingQuoteRequest!.DestinationPostalCode);
        Assert.Contains("Escolha a entrega", await posted.Content.ReadAsStringAsync());

        using var fragmentRequest = new HttpRequestMessage(HttpMethod.Post, "/checkout?handler=QuoteShipping")
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("FulfillmentMethod", "shipping"),
                new KeyValuePair<string, string>("ShippingAddress.PostalCode", "18000000"),
                new KeyValuePair<string, string>("__RequestVerificationToken", token)
            ])
        };
        fragmentRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var fragmentResponse = await client.SendAsync(fragmentRequest);
        var fragmentBody = await fragmentResponse.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, fragmentResponse.StatusCode);
        Assert.Contains("data-shipping-quote", fragmentBody);
        Assert.Contains("Escolha a entrega", fragmentBody);
        Assert.DoesNotContain("<main", fragmentBody, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task Validation_after_stock_loss_redirects_to_cart_with_preserved_cart_and_message()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 2)]));
        var api = new RecordingCheckout { CreateResults = new Queue<CheckoutResult>([CheckoutResult.Failure(CheckoutLoadState.Validation)]) };
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var initialQuote = Quote(offer, 2);
        var partialQuote = Quote(offer, 2, "insufficient");
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer, catalog: new StubCatalog(initialQuote, partialQuote));
        page.TempData = new TempDataDictionary(context, new TestTempDataProvider());
        page.Contact = new() { Name = "Customer", Email = "customer@example.com", Phone = "15999999999" };

        var result = await page.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Cart", redirect.PageName);
        Assert.Equal(2, Assert.Single(cart.Read().Lines).Quantity);
        Assert.Equal(0, cart.ClearCalls);
        Assert.Equal("A disponibilidade dos itens mudou. Ajuste as quantidades no carrinho antes de tentar novamente.", page.TempData["CartMessage"]?.ToString());
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task Validation_with_still_successful_quote_stays_on_checkout()
    {
        var offer = Guid.NewGuid();
        var cart = new TestCart(new(DateTimeOffset.UtcNow, [new(offer, 1)]));
        var api = new RecordingCheckout { CreateResults = new Queue<CheckoutResult>([CheckoutResult.Failure(CheckoutLoadState.Validation, "Confira os dados informados.")]) };
        var context = new DefaultHttpContext { RequestServices = Services() };
        var provider = DataProtectionProvider.Create(Directory.CreateTempSubdirectory(), c => c.SetApplicationName("Morita.LP.Razor"));
        var quote = Quote(offer, 1);
        var page = CreatePage(context, cart, api, new CheckoutDraftCookieStore(new HttpContextAccessor { HttpContext = context }, provider, new TestEnvironment(), TimeProvider.System), offer, catalog: new StubCatalog(quote, quote));
        page.TempData = new TempDataDictionary(context, new TestTempDataProvider());
        page.Contact = new() { Name = "Customer", Email = "customer@example.com", Phone = "15999999999" };

        var result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(CheckoutLoadState.Validation, page.ErrorState);
        Assert.Equal(CatalogLoadState.Success, page.Quote.State);
        Assert.Null(page.TempData["CartMessage"]);
        Assert.Equal(0, cart.ClearCalls);
    }

    private static CatalogQuoteResult Quote(Guid offer, int quantity, string availability = "available")
    {
        var available = availability == "available";
        return CatalogQuoteResult.Success("BRL", available ? 10 * quantity : 0, [new CatalogQuoteLine { PublicOfferId = offer, Quantity = quantity, Availability = availability, Presentation = "Kimono", Currency = "BRL", UnitPrice = 10, LinePrice = available ? 10 * quantity : null }]);
    }

    private static CheckoutModel CreatePage(DefaultHttpContext context, TestCart cart, RecordingCheckout api, ICheckoutDraftCookieStore draft, Guid offer, ICustomerAccountClient? account = null, ICustomerAccountCookieStore? accountCookies = null, IOptions<StorefrontOptions>? storefrontOptions = null, ICatalogClient? catalog = null)
    {
        var config = new CheckoutConfigurationResult(CheckoutLoadState.Success, new() { PickupEnabled = true, PublicPickupId = Guid.NewGuid(), Currency = "BRL", Pickup = new() { PublicPickupId = Guid.NewGuid(), DisplayName = "Loja", Address = new() { Street = "Rua", Number = "1", Neighborhood = "Centro", City = "Sorocaba", State = "SP", PostalCode = "18000-000" } } });
        var quote = Quote(offer, 1);
        var page = new CheckoutModel(cart, catalog ?? new StubCatalog(quote), api, draft, new NoopAccess(), new CheckoutRateLimiter(TimeProvider.System), account ?? new NoopAccount(), accountCookies ?? new NoopAccountCookie(), storefrontOptions)
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

    private sealed class StubCatalog : ICatalogClient
    {
        private readonly Queue<CatalogQuoteResult> quotes;

        public StubCatalog(params CatalogQuoteResult[] quotes) => this.quotes = new(quotes);

        public Task<CatalogResult> GetProductsAsync(string modality, CancellationToken cancellationToken = default) => Task.FromResult(CatalogResult.Empty());
        public Task<CatalogQuoteResult> QuoteAsync(CatalogQuoteRequest request, CancellationToken cancellationToken = default) => Task.FromResult(quotes.Count > 1 ? quotes.Dequeue() : quotes.Single());
    }

    private sealed class TestCart(CartState state) : ICartCookieStore
    {
        public CartState Read() => state;
        public int ClearCalls { get; private set; }
        public bool Add(Guid offerId, int quantity) => true;
        public bool Update(Guid offerId, int quantity) => true;
        public bool Remove(Guid offerId) => true;
        public bool Replace(IReadOnlyList<CartLine> lines) => true;
        public void Clear() => ClearCalls++;
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
        public int AddressReads { get; private set; }
        public AccountResult<CustomerAccountProfile>? ProfileResult { get; set; }
        public AccountResult<IReadOnlyList<CustomerAccountAddress>>? AddressesResult { get; set; }
        public AccountResult<CustomerAccountAddress> CreateAddressResult { get; set; } = AccountResult<CustomerAccountAddress>.Failure(AccountLoadState.Unavailable, "address unavailable");
        public AccountResult<bool> SetDefaultAddressResult { get; set; } = AccountResult<bool>.Failure(AccountLoadState.Unavailable, "default unavailable");
        public int CreateAddressCalls { get; private set; }
        public override Task<AccountResult<CustomerAccountProfile>> GetProfileAsync(string token, CancellationToken cancellationToken = default) { ProfileReads++; return Task.FromResult(ProfileResult ?? new AccountResult<CustomerAccountProfile>(AccountLoadState.Success, new() { Email = "customer@example.com", Name = "Customer", Phone = "15999999999" })); }
        public override Task<AccountResult<IReadOnlyList<CustomerAccountAddress>>> GetAddressesAsync(string token, CancellationToken cancellationToken = default) { AddressReads++; return Task.FromResult(AddressesResult ?? new AccountResult<IReadOnlyList<CustomerAccountAddress>>(AccountLoadState.Success, [address])); }
        public override Task<AccountResult<CustomerAccountAddress>> CreateAddressAsync(string token, CustomerAccountAddress savedAddress, CancellationToken cancellationToken = default) { CreateAddressCalls++; return Task.FromResult(CreateAddressResult); }
        public override Task<AccountResult<bool>> SetDefaultAddressAsync(string token, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(SetDefaultAddressResult);
    }

    private class NoopAccount : ICustomerAccountClient
    {
        public Task<AccountResult<AccountCodeChallenge>> RegisterAsync(string email, string name, string phone, string password, bool acceptedPrivacyPolicy, string privacyPolicyVersion, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<AccountCodeChallenge>.Failure(AccountLoadState.Unavailable));
        public Task<AccountResult<(CustomerAccountSession Session, CustomerAccountProfile Profile)>> VerifyEmailAsync(Guid challengeId, string code, bool acceptedPrivacyPolicy, string privacyPolicyVersion, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<(CustomerAccountSession, CustomerAccountProfile)>.Failure(AccountLoadState.Unavailable));
        public Task<AccountResult<(CustomerAccountSession Session, CustomerAccountProfile Profile)>> LoginAsync(string email, string password, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<(CustomerAccountSession, CustomerAccountProfile)>.Failure(AccountLoadState.Unavailable));
        public Task<AccountResult<AccountCodeChallenge>> RequestPasswordCodeAsync(string? token, string email, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<AccountCodeChallenge>.Failure(AccountLoadState.Unavailable));
        public Task<AccountResult<(CustomerAccountSession Session, CustomerAccountProfile Profile)>> ResetPasswordAsync(string? token, Guid challengeId, string code, string password, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<(CustomerAccountSession, CustomerAccountProfile)>.Failure(AccountLoadState.Unavailable));
        public virtual Task<AccountResult<bool>> CloseAsync(string token, string currentPassword, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<bool>(AccountLoadState.Success, true));
        public virtual Task<AccountResult<CustomerAccountProfile>> GetProfileAsync(string token, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<CustomerAccountProfile>(AccountLoadState.Success, new() { Email = "customer@example.com", Name = "Customer", Phone = "15999999999" }));
        public virtual Task<AccountResult<bool>> UpdateProfileAsync(string token, string? name, string? phone, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<bool>(AccountLoadState.Success, true));
        public virtual Task<AccountResult<IReadOnlyList<CustomerAccountAddress>>> GetAddressesAsync(string token, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<IReadOnlyList<CustomerAccountAddress>>(AccountLoadState.Success, []));
        public virtual Task<AccountResult<CustomerAccountAddress>> CreateAddressAsync(string token, CustomerAccountAddress address, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<CustomerAccountAddress>.Failure(AccountLoadState.Unavailable));
        public virtual Task<AccountResult<CustomerAccountAddress>> UpdateAddressAsync(string token, Guid id, CustomerAccountAddress address, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<CustomerAccountAddress>.Failure(AccountLoadState.Unavailable));
        public virtual Task<AccountResult<bool>> DeleteAddressAsync(string token, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<bool>.Failure(AccountLoadState.Unavailable));
        public virtual Task<AccountResult<bool>> SetDefaultAddressAsync(string token, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(AccountResult<bool>.Failure(AccountLoadState.Unavailable));
        public virtual Task<AccountResult<StorefrontAccountOrderPage>> GetOrdersAsync(string token, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) => Task.FromResult(new AccountResult<StorefrontAccountOrderPage>(AccountLoadState.Success, new() { Page = page, PageSize = pageSize }));
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
