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

    private static CheckoutModel CreatePage(DefaultHttpContext context, TestCart cart, RecordingCheckout api, ICheckoutDraftCookieStore draft, Guid offer)
    {
        var config = new CheckoutConfigurationResult(CheckoutLoadState.Success, new() { PickupEnabled = true, PublicPickupId = Guid.NewGuid(), Currency = "BRL", Pickup = new() { PublicPickupId = Guid.NewGuid(), DisplayName = "Loja", Address = new() { Street = "Rua", Number = "1", Neighborhood = "Centro", City = "Sorocaba", State = "SP", PostalCode = "18000-000" } } });
        var quote = CatalogQuoteResult.Success("BRL", 10, [new CatalogQuoteLine { PublicOfferId = offer, Quantity = 1, Availability = "available", Presentation = "Kimono", Currency = "BRL", UnitPrice = 10, LinePrice = 10 }]);
        var page = new CheckoutModel(cart, new StubCatalog(quote), api, draft, new NoopAccess(), new CheckoutRateLimiter(TimeProvider.System))
        {
            Contact = new CheckoutModel.ContactInput()
        };
        api.Configuration = config;
        page.PageContext = new PageContext(new Microsoft.AspNetCore.Mvc.ActionContext(context, new RouteData(), new PageActionDescriptor()));
        page.PageContext.ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());
        return page;
    }

    private static IServiceProvider Services() => new ServiceCollection().AddSingleton<IHostEnvironment>(new TestEnvironment()).BuildServiceProvider();

    private sealed class RecordingCheckout : ICheckoutClient
    {
        public CheckoutConfigurationResult Configuration { get; set; } = CheckoutConfigurationResult.Failure(CheckoutLoadState.Unavailable);
        public List<(string Key, string Token)> Credentials { get; } = [];
        public Task<CheckoutConfigurationResult> GetConfigurationAsync(CancellationToken cancellationToken = default) => Task.FromResult(Configuration);
        public Task<CheckoutResult> CreateAsync(CheckoutCreateRequest request, string idempotencyKey, string accessToken, CancellationToken cancellationToken = default) { Credentials.Add((idempotencyKey, accessToken)); return Task.FromResult(CheckoutResult.Failure(CheckoutLoadState.Timeout, "timeout")); }
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

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ApplicationVersion { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
