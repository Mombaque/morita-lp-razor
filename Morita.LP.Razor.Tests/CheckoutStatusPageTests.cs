using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Pages;
using Morita.LP.Razor.Services;
using Xunit;

namespace Morita.LP.Razor.Tests;

public sealed class CheckoutStatusPageTests
{
    [Fact]
    public async Task Completed_checkout_conversion_redirects_and_writes_order_access()
    {
        var id = Guid.NewGuid(); var api = new FakeCheckout { Checkout = Checkout(id, "completed"), Payment = new(PaymentLoadState.Success, new PixPayment { Status = "converted", Amount = 10, Currency = "BRL", ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1), PublicOrderNumber = "MF-0123456789ABCDEF" }) }; var order = new FakeOrderAccess();
        var page = Create(id, api, order);
        var result = await page.OnGetAsync(CancellationToken.None);
        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Order", redirect.PageName);
        Assert.Equal("MF-0123456789ABCDEF", order.Number);
    }

    [Fact]
    public async Task Unknown_payment_does_not_fall_through_to_checkout_cancel()
    {
        var id = Guid.NewGuid(); var api = new FakeCheckout { Checkout = Checkout(id, "active"), Payment = PaymentResult.Failure(PaymentLoadState.Timeout) }; var page = Create(id, api, new FakeOrderAccess());
        await page.OnPostCancelAsync(CancellationToken.None);
        Assert.Equal(0, api.CheckoutCancelCount);
        Assert.Equal(0, api.PaymentCancelCount);
    }

    [Fact]
    public async Task Active_payment_uses_payment_cancel_endpoint()
    {
        var id = Guid.NewGuid(); var api = new FakeCheckout { Checkout = Checkout(id, "active"), Payment = new(PaymentLoadState.Success, new PixPayment { Status = "pending", Amount = 10, Currency = "BRL", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5), PixCopyPaste = "x", QrCodePngDataUri = "data:image/png;base64,x" }) }; var page = Create(id, api, new FakeOrderAccess());
        await page.OnPostCancelAsync(CancellationToken.None);
        Assert.Equal(0, api.CheckoutCancelCount);
        Assert.Equal(1, api.PaymentCancelCount);
    }

    [Fact]
    public async Task Cancellation_pending_does_not_call_cancel_again_and_exposes_safe_message()
    {
        var id = Guid.NewGuid();
        var api = new FakeCheckout { Checkout = Checkout(id, "paymentpending"), Payment = new(PaymentLoadState.Success, new PixPayment { Status = "cancellationpending", Amount = 10, Currency = "BRL", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) }) };
        var page = Create(id, api, new FakeOrderAccess());

        await page.OnGetAsync(CancellationToken.None);
        Assert.Equal(0, api.PaymentCancelCount);
        Assert.Contains("confirmando o cancelamento", page.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", page.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("paymentpending", "pending")]
    [InlineData("cancelled", "cancelled")]
    [InlineData("refunded", "refunded")]
    public async Task Owned_payment_lifecycle_statuses_keep_the_payment_card_data(string checkoutStatus, string paymentStatus)
    {
        var id = Guid.NewGuid(); var api = new FakeCheckout { Checkout = Checkout(id, checkoutStatus), Payment = new(PaymentLoadState.Success, new PixPayment { Status = paymentStatus, Amount = 10, Currency = "BRL", ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) }) }; var page = Create(id, api, new FakeOrderAccess());
        var result = await page.OnGetAsync(CancellationToken.None);
        Assert.IsType<PageResult>(result);
        Assert.Equal(paymentStatus, page.Payment!.Status);
    }

    private static CheckoutStatusModel Create(Guid id, FakeCheckout api, FakeOrderAccess order)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<IHostEnvironment>(new TestEnvironment()).BuildServiceProvider() };
        var page = new CheckoutStatusModel(api, new FakeAccess(id), new FakeAttempt(), order, new CheckoutRateLimiter(TimeProvider.System)) { PublicCheckoutId = id };
        page.PageContext = new PageContext(new Microsoft.AspNetCore.Mvc.ActionContext(context, new RouteData(), new PageActionDescriptor())) { ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) };
        return page;
    }
    private static CheckoutResponse Checkout(Guid id, string status) => new() { PublicCheckoutId = id, Status = status, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), AccessExpiresAt = DateTimeOffset.UtcNow.AddDays(30), Currency = "BRL", Total = 10, MerchandiseTotal = 10, Pickup = new PickupSnapshot { PublicPickupId = Guid.NewGuid(), DisplayName = "Loja", Address = new CheckoutAddress { Street = "Rua", Number = "1", Neighborhood = "Centro", City = "Sorocaba", State = "SP", PostalCode = "18000-000" } }, Contact = new CheckoutContact { Name = "Ana", Email = "a@a.com", Phone = "1" }, Lines = [new CheckoutLine { PublicOfferId = Guid.NewGuid(), Quantity = 1, Presentation = "Item", UnitPrice = 10, LineTotal = 10 }] };
    private sealed class FakeCheckout : ICheckoutClient { public CheckoutResponse? Checkout; public PaymentResult Payment = PaymentResult.Failure(PaymentLoadState.NotFound); public int CheckoutCancelCount; public int PaymentCancelCount; public Task<CheckoutConfigurationResult> GetConfigurationAsync(CancellationToken c = default) => Task.FromResult(CheckoutConfigurationResult.Failure(CheckoutLoadState.Unavailable)); public Task<CheckoutResult> CreateAsync(CheckoutCreateRequest r, string i, string a, CancellationToken c = default) => Task.FromResult(CheckoutResult.Failure(CheckoutLoadState.Unavailable)); public Task<CheckoutResult> GetAsync(Guid i, string a, CancellationToken c = default) => Task.FromResult(new CheckoutResult(CheckoutLoadState.Success, Checkout)); public Task<CheckoutResult> CancelAsync(Guid i, string a, CancellationToken c = default) { CheckoutCancelCount++; return Task.FromResult(new CheckoutResult(CheckoutLoadState.Success, null)); } public Task<PaymentResult> GetPaymentAsync(Guid i, string a, CancellationToken c = default) => Task.FromResult(Payment); public Task<PaymentResult> CancelPaymentAsync(Guid i, string a, CancellationToken c = default) { PaymentCancelCount++; return Task.FromResult(PaymentResult.Failure(PaymentLoadState.Success)); } }
    private sealed class FakeAccess(Guid id) : ICheckoutAccessCookieStore { public CheckoutAccess? Read(Guid value) => value == id ? new(value, new string('t', 32), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30)) : null; public bool Write(CheckoutResponse c, string t) => true; public void Clear() { } }
    private sealed class FakeAttempt : IPaymentAttemptCookieStore { public PaymentAttempt? Read(Guid id) => null; public PaymentAttempt Ensure(Guid id) => new(id, new string('i', 32), DateTimeOffset.UtcNow); public void Clear(Guid id) { } }
    private sealed class FakeOrderAccess : IOrderAccessCookieStore { public string? Number; public OrderAccess? Read(string n) => null; public bool Write(string n, string t) { Number = n; return true; } public void Clear() { } }
    private sealed class TestEnvironment : IHostEnvironment { public string EnvironmentName { get; set; } = Environments.Development; public string ApplicationName { get; set; } = "tests"; public string ApplicationVersion { get; set; } = "tests"; public string ContentRootPath { get; set; } = AppContext.BaseDirectory; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }
}
