using System;
using System.Collections.Generic;
using System.Linq;
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

    [Fact]
    public async Task Successful_pix_initiation_does_not_show_a_failure_message()
    {
        var id = Guid.NewGuid();
        var api = new FakeCheckout
        {
            Checkout = Checkout(id, "active"),
            Initiation = new(PaymentLoadState.Success, new PixPayment
            {
                Status = "pending",
                Amount = 10,
                Currency = "BRL",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                PixCopyPaste = "000201010212FAKE",
                QrCodePngDataUri = "data:image/png;base64,valid"
            })
        };
        var page = Create(id, api, new FakeOrderAccess());

        await page.OnPostPayPixAsync(CancellationToken.None);

        Assert.Equal(PaymentLoadState.Success, page.PaymentState);
        Assert.Equal("pending", page.Payment!.Status);
        Assert.Null(page.Message);
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

    [Theory]
    [InlineData(PaymentLoadState.Validation, "dados atuais")]
    [InlineData(PaymentLoadState.Conflict, "tentativa de pagamento PIX mudou")]
    public async Task Pix_initiation_errors_use_payment_specific_messages(PaymentLoadState state, string expectedMessage)
    {
        var id = Guid.NewGuid();
        var api = new FakeCheckout
        {
            Checkout = Checkout(id, "active"),
            Initiation = PaymentResult.Failure(state)
        };
        var page = Create(id, api, new FakeOrderAccess());

        await page.OnPostPayPixAsync(CancellationToken.None);

        Assert.Equal(state, page.PaymentState);
        Assert.Contains(expectedMessage, page.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, api.CheckoutCancelCount);
        Assert.Equal(0, api.PaymentCancelCount);
    }

    [Fact]
    public async Task Successful_card_initiation_redirects_to_hosted_checkout()
    {
        var id = Guid.NewGuid();
        const string hostedUrl = "http://127.0.0.1/v1/testing/online-payments/hosted/ref";
        var api = new FakeCheckout
        {
            Checkout = Checkout(id, "active"),
            CardInitiation = new(PaymentLoadState.Success, new PixPayment
            {
                Status = "pending",
                Method = OnlinePaymentMethod.Card,
                Amount = 10,
                Currency = "BRL",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                CheckoutUrl = hostedUrl
            })
        };
        var page = Create(id, api, new FakeOrderAccess());

        var result = await page.OnPostPayCardAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal(hostedUrl, redirect.Url);
        Assert.Equal(1, api.CardInitiationCount);
        Assert.Null(page.Message);
    }

    [Fact]
    public async Task Card_initiation_does_not_redirect_to_a_disallowed_checkout_url()
    {
        var id = Guid.NewGuid();
        var api = new FakeCheckout
        {
            Checkout = Checkout(id, "active"),
            CardInitiation = new(PaymentLoadState.Success, new PixPayment
            {
                Status = "pending",
                Method = OnlinePaymentMethod.Card,
                Amount = 10,
                Currency = "BRL",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                CheckoutUrl = "http://example.com/checkout"
            })
        };
        var page = Create(id, api, new FakeOrderAccess());

        var result = await page.OnPostPayCardAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(1, api.CardInitiationCount);
        Assert.Equal("http://example.com/checkout", page.Payment!.CheckoutUrl);
    }

    [Fact]
    public async Task Converted_card_initiation_still_redirects_to_the_order()
    {
        var id = Guid.NewGuid();
        var api = new FakeCheckout
        {
            Checkout = Checkout(id, "active"),
            CardInitiation = new(PaymentLoadState.Success, new PixPayment
            {
                Status = "converted",
                Method = OnlinePaymentMethod.Card,
                Amount = 10,
                Currency = "BRL",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                PublicOrderNumber = "MF-0123456789ABCDEF"
            })
        };
        var order = new FakeOrderAccess();
        var page = Create(id, api, order);

        var result = await page.OnPostPayCardAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Order", redirect.PageName);
        Assert.Equal("MF-0123456789ABCDEF", order.Number);
    }

    [Fact]
    public async Task Retry_card_rotates_attempt_key_and_redirects_to_hosted_checkout()
    {
        var id = Guid.NewGuid();
        const string hostedUrl = "http://127.0.0.1/v1/testing/online-payments/hosted/retry";
        var api = new FakeCheckout
        {
            Checkout = Checkout(id, "paymentpending"),
            Payment = new(PaymentLoadState.Success, new PixPayment
            {
                Status = "failed",
                Method = OnlinePaymentMethod.Card,
                Amount = 10,
                Currency = "BRL",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
            }),
            CardInitiation = new(PaymentLoadState.Success, new PixPayment
            {
                Status = "pending",
                Method = OnlinePaymentMethod.Card,
                Amount = 10,
                Currency = "BRL",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                CheckoutUrl = hostedUrl
            })
        };
        var attempts = new FakeAttempt();
        var page = Create(id, api, new FakeOrderAccess(), new FakeCart(), attempts);

        var result = await page.OnPostRetryCardAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal(hostedUrl, redirect.Url);
        Assert.Equal(1, api.CardInitiationCount);
        Assert.Equal(new string('r', 32), api.LastInitiationKey);
    }

    [Fact]
    public async Task Expired_checkout_restores_previous_items_and_redirects_to_checkout()
    {
        var id = Guid.NewGuid();
        var checkout = Checkout(id, "expired");
        var api = new FakeCheckout
        {
            Checkout = checkout,
            Payment = new(PaymentLoadState.Success, new PixPayment { Status = "expired", Amount = 10, Currency = "BRL", ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) })
        };
        var cart = new FakeCart();
        var page = Create(id, api, new FakeOrderAccess(), cart);

        var result = await page.OnPostRestoreCheckoutAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Checkout", redirect.PageName);
        Assert.Equal(checkout.Lines.Select(line => new CartLine(line.PublicOfferId, line.Quantity)), cart.ReplacedLines);
    }

    [Fact]
    public async Task Expired_checkout_preserves_existing_cart_until_customer_restores_previous_items()
    {
        var id = Guid.NewGuid();
        var currentLine = new CartLine(Guid.NewGuid(), 1);
        var cart = new FakeCart(new CartState(DateTimeOffset.UtcNow, [currentLine]));
        var api = new FakeCheckout
        {
            Checkout = Checkout(id, "expired"),
            Payment = new(PaymentLoadState.Success, new PixPayment { Status = "expired", Amount = 10, Currency = "BRL", ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) })
        };
        var page = Create(id, api, new FakeOrderAccess(), cart);

        await page.OnGetAsync(CancellationToken.None);

        Assert.True(page.HasCurrentCart);
        Assert.Null(cart.ReplacedLines);
        Assert.Equal([currentLine], cart.Read().Lines);
    }

    [Fact]
    public async Task Retry_pix_rotates_attempt_key_for_an_expired_payment_on_active_checkout()
    {
        var id = Guid.NewGuid();
        var api = new FakeCheckout
        {
            Checkout = Checkout(id, "paymentpending"),
            Payment = new(PaymentLoadState.Success, new PixPayment { Status = "expired", Amount = 10, Currency = "BRL", ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) }),
            Initiation = new(PaymentLoadState.Success, new PixPayment { Status = "pending", Amount = 10, Currency = "BRL", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15), PixCopyPaste = "pix", QrCodePngDataUri = "data:image/png;base64,x" })
        };
        var attempts = new FakeAttempt();
        var page = Create(id, api, new FakeOrderAccess(), new FakeCart(), attempts);

        var result = await page.OnPostRetryPixAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(1, api.InitiationCount);
        Assert.Equal(new string('r', 32), api.LastInitiationKey);
        Assert.NotEqual(new string('i', 32), api.LastInitiationKey);
    }

    [Fact]
    public async Task Retry_pix_does_not_create_a_new_attempt_when_payment_is_still_processing()
    {
        var id = Guid.NewGuid();
        var api = new FakeCheckout
        {
            Checkout = Checkout(id, "paymentpending"),
            Payment = new(PaymentLoadState.Success, new PixPayment { Status = "processing", Amount = 10, Currency = "BRL", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) })
        };
        var page = Create(id, api, new FakeOrderAccess(), new FakeCart());

        var result = await page.OnPostRetryPixAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(0, api.InitiationCount);
        Assert.Contains("não pode ser gerado novamente", page.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CheckoutStatusModel Create(Guid id, FakeCheckout api, FakeOrderAccess order, FakeCart? cart = null, FakeAttempt? attempt = null)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<IHostEnvironment>(new TestEnvironment()).BuildServiceProvider() };
        var page = new CheckoutStatusModel(api, new FakeAccess(id), attempt ?? new FakeAttempt(), order, cart ?? new FakeCart(), new CheckoutRateLimiter(TimeProvider.System)) { PublicCheckoutId = id };
        page.PageContext = new PageContext(new Microsoft.AspNetCore.Mvc.ActionContext(context, new RouteData(), new PageActionDescriptor())) { ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) };
        return page;
    }
    private static CheckoutResponse Checkout(Guid id, string status) => new() { PublicCheckoutId = id, Status = status, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), AccessExpiresAt = DateTimeOffset.UtcNow.AddDays(30), Currency = "BRL", Total = 10, MerchandiseTotal = 10, Pickup = new PickupSnapshot { PublicPickupId = Guid.NewGuid(), DisplayName = "Loja", Address = new CheckoutAddress { Street = "Rua", Number = "1", Neighborhood = "Centro", City = "Sorocaba", State = "SP", PostalCode = "18000-000" } }, Contact = new CheckoutContact { Name = "Ana", Email = "a@a.com", Phone = "1" }, Lines = [new CheckoutLine { PublicOfferId = Guid.NewGuid(), Quantity = 1, Presentation = "Item", UnitPrice = 10, LineTotal = 10 }] };
    private sealed class FakeCheckout : ICheckoutClient
    {
        public CheckoutResponse? Checkout;
        public PaymentResult Payment = PaymentResult.Failure(PaymentLoadState.NotFound);
        public PaymentResult Initiation = PaymentResult.Failure(PaymentLoadState.Unavailable);
        public PaymentResult CardInitiation = PaymentResult.Failure(PaymentLoadState.Unavailable);
        public int CheckoutCancelCount;
        public int PaymentCancelCount;
        public int InitiationCount;
        public int CardInitiationCount;
        public string? LastInitiationKey;
        public Task<CheckoutConfigurationResult> GetConfigurationAsync(CancellationToken c = default) => Task.FromResult(CheckoutConfigurationResult.Failure(CheckoutLoadState.Unavailable));
        public Task<CheckoutResult> CreateAsync(CheckoutCreateRequest r, string i, string a, CancellationToken c = default) => Task.FromResult(CheckoutResult.Failure(CheckoutLoadState.Unavailable));
        public Task<CheckoutResult> GetAsync(Guid i, string a, CancellationToken c = default) => Task.FromResult(new CheckoutResult(CheckoutLoadState.Success, Checkout));
        public Task<CheckoutResult> CancelAsync(Guid i, string a, CancellationToken c = default) { CheckoutCancelCount++; return Task.FromResult(new CheckoutResult(CheckoutLoadState.Success, null)); }
        public Task<PaymentResult> InitiatePixAsync(Guid i, string a, string k, CancellationToken c = default) { InitiationCount++; LastInitiationKey = k; return Task.FromResult(Initiation); }
        public Task<PaymentResult> InitiateCardAsync(Guid i, string a, string k, CancellationToken c = default) { CardInitiationCount++; LastInitiationKey = k; return Task.FromResult(CardInitiation); }
        public Task<PaymentResult> GetPaymentAsync(Guid i, string a, CancellationToken c = default) => Task.FromResult(Payment);
        public Task<PaymentResult> CancelPaymentAsync(Guid i, string a, CancellationToken c = default) { PaymentCancelCount++; return Task.FromResult(PaymentResult.Failure(PaymentLoadState.Success)); }
    }
    private sealed class FakeAccess(Guid id) : ICheckoutAccessCookieStore { public CheckoutAccess? Read(Guid value) => value == id ? new(value, new string('t', 32), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30)) : null; public bool Write(CheckoutResponse c, string t) => true; public void Clear() { } }
    private sealed class FakeAttempt : IPaymentAttemptCookieStore { public PaymentAttempt? Read(Guid id) => null; public PaymentAttempt Ensure(Guid id) => new(id, new string('i', 32), DateTimeOffset.UtcNow); public PaymentAttempt Rotate(Guid id) => new(id, new string('r', 32), DateTimeOffset.UtcNow); public void Clear(Guid id) { } }
    private sealed class FakeCart(CartState? initial = null) : ICartCookieStore { private CartState state = initial ?? new(DateTimeOffset.UtcNow, []); public IReadOnlyList<CartLine>? ReplacedLines { get; private set; } public CartState Read() => state; public bool Add(Guid id, int quantity) => true; public bool Update(Guid id, int quantity) => true; public bool Remove(Guid id) => true; public bool Replace(IReadOnlyList<CartLine> lines) { ReplacedLines = lines; state = new(DateTimeOffset.UtcNow, lines); return true; } public void Clear() { } }
    private sealed class FakeOrderAccess : IOrderAccessCookieStore { public string? Number; public OrderAccess? Read(string n) => null; public bool Write(string n, string t) { Number = n; return true; } public void Clear() { } }
    private sealed class TestEnvironment : IHostEnvironment { public string EnvironmentName { get; set; } = Environments.Development; public string ApplicationName { get; set; } = "tests"; public string ApplicationVersion { get; set; } = "tests"; public string ContentRootPath { get; set; } = AppContext.BaseDirectory; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }
}
