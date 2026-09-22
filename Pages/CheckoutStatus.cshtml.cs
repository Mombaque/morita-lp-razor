using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class CheckoutStatusModel(ICheckoutClient client, ICheckoutAccessCookieStore access, IPaymentAttemptCookieStore paymentAttempt, IOrderAccessCookieStore orderAccess, ICartCookieStore cart, CheckoutRateLimiter rateLimiter) : PageModel
{
    public CheckoutResponse? Checkout { get; private set; }
    public CheckoutLoadState State { get; private set; } = CheckoutLoadState.NotFound;
    public string? Message { get; private set; }
    public string? AccountMessage { get; private set; }
    public PixPayment? Payment { get; private set; }
    public PaymentLoadState PaymentState { get; private set; } = PaymentLoadState.NotFound;
    public bool HasCurrentCart { get; private set; }
    public IReadOnlyList<OnlinePaymentMethod> OnlinePaymentMethods { get; private set; } = [];
    public bool PixAvailable => OnlinePaymentMethods.Contains(OnlinePaymentMethod.Pix);
    public bool CardAvailable => OnlinePaymentMethods.Contains(OnlinePaymentMethod.Card);
    public bool HasOnlinePaymentMethods => OnlinePaymentMethods.Count > 0;
    [BindProperty(SupportsGet = true)] public Guid PublicCheckoutId { get; set; }
    [BindProperty(SupportsGet = true)] public OnlinePaymentMethod PaymentMethod { get; set; } = OnlinePaymentMethod.Pix;

    private bool IsAjaxPaymentRequest => string.Equals(Request.Headers["X-Requested-With"].ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        var tempDataFactory = HttpContext.RequestServices.GetService<ITempDataDictionaryFactory>();
        AccountMessage = tempDataFactory?.GetTempData(HttpContext)["CheckoutAccountMessage"] as string;
        return await LoadOwnedAsync(cancellationToken) ?? Page();
    }

    public async Task<IActionResult> OnPostPayPixAsync(CancellationToken cancellationToken)
    {
        var credential = access.Read(PublicCheckoutId);
        if (credential is null) return Inaccessible();
        return await InitiatePixAsync(credential.Token, paymentAttempt.Ensure(PublicCheckoutId).IdempotencyKey, cancellationToken);
    }

    public async Task<IActionResult> OnPostPayCardAsync(CancellationToken cancellationToken)
    {
        var credential = access.Read(PublicCheckoutId);
        if (credential is null) return Inaccessible();
        return await InitiateCardAsync(credential.Token, paymentAttempt.Ensure(PublicCheckoutId).IdempotencyKey, cancellationToken);
    }

    public async Task<IActionResult> OnPostRetryPixAsync(CancellationToken cancellationToken)
    {
        var credential = access.Read(PublicCheckoutId);
        if (credential is null) return Inaccessible();

        var checkoutResult = await client.GetAsync(PublicCheckoutId, credential.Token, cancellationToken);
        var paymentResult = await client.GetPaymentAsync(PublicCheckoutId, credential.Token, cancellationToken);
        if (checkoutResult.State != CheckoutLoadState.Success || checkoutResult.Checkout is null ||
            paymentResult.State != PaymentLoadState.Success || paymentResult.Payment is not { } payment ||
            checkoutResult.Checkout.Status is not ("active" or "paymentpending") ||
            payment.Status is not ("expired" or "failed" or "cancelled"))
        {
            await LoadOwnedAsync(cancellationToken);
            Message = "O pagamento PIX não pode ser gerado novamente neste momento.";
            return IsAjaxPaymentRequest ? PaymentFlowFragment() : Page();
        }

        return await InitiatePixAsync(credential.Token, paymentAttempt.Rotate(PublicCheckoutId).IdempotencyKey, cancellationToken);
    }

    public async Task<IActionResult> OnPostRetryCardAsync(CancellationToken cancellationToken)
    {
        var credential = access.Read(PublicCheckoutId);
        if (credential is null) return Inaccessible();

        var checkoutResult = await client.GetAsync(PublicCheckoutId, credential.Token, cancellationToken);
        var paymentResult = await client.GetPaymentAsync(PublicCheckoutId, credential.Token, cancellationToken);
        if (checkoutResult.State != CheckoutLoadState.Success || checkoutResult.Checkout is null ||
            paymentResult.State != PaymentLoadState.Success || paymentResult.Payment is not { } payment ||
            !payment.IsCard ||
            checkoutResult.Checkout.Status is not ("active" or "paymentpending") ||
            payment.Status is not ("expired" or "failed" or "cancelled"))
        {
            await LoadOwnedAsync(cancellationToken);
            Message = "O pagamento com cartão não pode ser gerado novamente neste momento.";
            return IsAjaxPaymentRequest ? PaymentFlowFragment() : Page();
        }

        return await InitiateCardAsync(credential.Token, paymentAttempt.Rotate(PublicCheckoutId).IdempotencyKey, cancellationToken);
    }

    public async Task<IActionResult> OnPostRestoreCheckoutAsync(CancellationToken cancellationToken)
    {
        var credential = access.Read(PublicCheckoutId);
        if (credential is null) return Inaccessible();

        var checkoutResult = await client.GetAsync(PublicCheckoutId, credential.Token, cancellationToken);
        if (checkoutResult.State != CheckoutLoadState.Success || checkoutResult.Checkout is not { Status: "expired" } checkout)
        {
            await LoadOwnedAsync(cancellationToken);
            Message = "Este checkout não pode ser refeito neste momento.";
            return Page();
        }

        var paymentResult = await client.GetPaymentAsync(PublicCheckoutId, credential.Token, cancellationToken);
        if (paymentResult.State != PaymentLoadState.NotFound &&
            (paymentResult.State != PaymentLoadState.Success || paymentResult.Payment is { Status: not ("expired" or "failed" or "cancelled") }))
        {
            await LoadOwnedAsync(cancellationToken);
            Message = "Este checkout não pode ser refeito porque o pagamento ainda pode ser processado.";
            return Page();
        }

        if (!cart.Replace(checkout.Lines.Select(line => new CartLine(line.PublicOfferId, line.Quantity)).ToList()))
        {
            await LoadOwnedAsync(cancellationToken);
            Message = "Não foi possível restaurar os itens deste checkout. Volte aos produtos e monte o carrinho novamente.";
            return Page();
        }

        return RedirectToPage("/Checkout");
    }

    private async Task<IActionResult> InitiatePixAsync(string accessToken, string idempotencyKey, CancellationToken cancellationToken)
    {
        var result = await client.InitiatePixAsync(PublicCheckoutId, accessToken, idempotencyKey, cancellationToken);
        return await CompleteInitiationAsync(result, OnlinePaymentMethod.Pix, accessToken, cancellationToken);
    }

    private async Task<IActionResult> InitiateCardAsync(string accessToken, string idempotencyKey, CancellationToken cancellationToken)
    {
        var result = await client.InitiateCardAsync(PublicCheckoutId, accessToken, idempotencyKey, cancellationToken);
        return await CompleteInitiationAsync(result, OnlinePaymentMethod.Card, accessToken, cancellationToken);
    }

    private async Task<IActionResult> CompleteInitiationAsync(PaymentResult result, OnlinePaymentMethod method, string accessToken, CancellationToken cancellationToken)
    {
        if (result.State == PaymentLoadState.Success && result.Payment is { PublicOrderNumber: { } number } && result.Payment.Status == "converted")
        {
            if (orderAccess.Write(number, accessToken))
            {
                if (IsAjaxPaymentRequest) return new JsonResult(new { redirectUrl = Url.Page("/Order", new { publicOrderNumber = number }) });
                return RedirectToPage("/Order", new { publicOrderNumber = number });
            }
            return Inaccessible();
        }

        if (result.State == PaymentLoadState.Success
            && result.Payment is { Status: "pending", CheckoutUrl: { } checkoutUrl }
            && StorefrontHostedCheckoutUrl.IsAllowed(checkoutUrl))
        {
            if (IsAjaxPaymentRequest) return new JsonResult(new { redirectUrl = checkoutUrl });
            return Redirect(checkoutUrl);
        }

        await LoadOwnedAsync(cancellationToken);
        PaymentState = result.State;
        Payment = result.Payment;
        PaymentMethod = method;
        Message = result.Message ?? (result.State == PaymentLoadState.Success ? null : PaymentMessage(result.State, method));
        return IsAjaxPaymentRequest ? PaymentFlowFragment() : Page();
    }

    public async Task<IActionResult> OnGetPaymentAsync(CancellationToken cancellationToken, string? format)
    {
        var credential = access.Read(PublicCheckoutId);
        if (credential is null)
        {
            if (string.Equals(format, "fragment", StringComparison.OrdinalIgnoreCase) && IsAjaxPaymentRequest) return Inaccessible();
            return new JsonResult(new { state = "inaccessible" }) { StatusCode = StatusCodes.Status404NotFound };
        }
        var result = await client.GetPaymentAsync(PublicCheckoutId, credential.Token, cancellationToken);
        if (result.State == PaymentLoadState.Success && result.Payment is { PublicOrderNumber: { } number } && result.Payment.Status == "converted" && orderAccess.Write(number, credential.Token))
        {
            var orderUrl = Url.Page("/Order", new { publicOrderNumber = number });
            return IsAjaxPaymentRequest
                ? new JsonResult(new { state = "converted", redirectUrl = orderUrl })
                : new JsonResult(new { state = "converted", url = orderUrl });
        }
        if (string.Equals(format, "fragment", StringComparison.OrdinalIgnoreCase))
        {
            var loaded = await LoadOwnedAsync(cancellationToken);
            if (loaded is RedirectToPageResult && Payment?.PublicOrderNumber is { } orderNumber)
            {
                return new JsonResult(new { redirectUrl = Url.Page("/Order", new { publicOrderNumber = orderNumber }) });
            }

            return PaymentFlowFragment();
        }
        return new JsonResult(new { state = result.State.ToString().ToLowerInvariant(), status = result.Payment?.Status, expiresAt = result.Payment?.ExpiresAt });
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken cancellationToken)
    {
        var credential = access.Read(PublicCheckoutId);
        if (credential is null) return Inaccessible();
        if (!rateLimiter.TryConsume(ClientIdentityResolver.Resolve(HttpContext, HttpContext.RequestServices.GetRequiredService<IHostEnvironment>()), "checkout-cancel"))
        {
            await LoadOwnedAsync(cancellationToken);
            State = CheckoutLoadState.RateLimited;
            Message = "Muitas tentativas. Aguarde um pouco.";
            return IsAjaxPaymentRequest ? PaymentFlowFragment() : Page();
        }
        var currentPayment = await client.GetPaymentAsync(PublicCheckoutId, credential.Token, cancellationToken);
        if (currentPayment.State == PaymentLoadState.Success && currentPayment.Payment is { Status: "pending" or "processing" or "approved" or "conversionpending" })
        {
            var paymentCancellation = await client.CancelPaymentAsync(PublicCheckoutId, credential.Token, cancellationToken);
            if (paymentCancellation.State == PaymentLoadState.Success)
            {
                if (IsAjaxPaymentRequest) return await RefreshPaymentFlowAsync(cancellationToken);
                return RedirectToPage(new { publicCheckoutId = PublicCheckoutId });
            }
            await LoadOwnedAsync(cancellationToken); PaymentState = paymentCancellation.State; Payment = paymentCancellation.Payment; Message = PaymentLifecycleMessage(Payment) ?? "O pagamento não pode ser cancelado neste momento."; return IsAjaxPaymentRequest ? PaymentFlowFragment() : Page();
        }
        if (currentPayment.State != PaymentLoadState.NotFound)
        {
            await LoadOwnedAsync(cancellationToken);
            PaymentState = currentPayment.State;
            Message = PaymentLifecycleMessage(currentPayment.Payment) ?? "O pagamento não pode ser cancelado neste momento.";
            return IsAjaxPaymentRequest ? PaymentFlowFragment() : Page();
        }
        var result = await client.CancelAsync(PublicCheckoutId, credential.Token, cancellationToken);
        if (result.State == CheckoutLoadState.Success)
        {
            if (IsAjaxPaymentRequest) return await RefreshPaymentFlowAsync(cancellationToken);
            return RedirectToPage(new { publicCheckoutId = PublicCheckoutId });
        }
        if (result.State == CheckoutLoadState.NotFound)
        {
            access.Clear();
            State = result.State;
            Checkout = null;
            Message = "Esta reserva não está disponível ou já expirou.";
            return IsAjaxPaymentRequest ? PaymentFlowFragment() : Page();
        }

        var errorMessage = result.Message ?? "A reserva não pode ser cancelada neste momento.";
        await LoadOwnedAsync(cancellationToken);
        if (Checkout is not null)
        {
            State = result.State;
            Message = errorMessage;
        }
        return IsAjaxPaymentRequest ? PaymentFlowFragment() : Page();
    }

    private async Task<IActionResult?> LoadOwnedAsync(CancellationToken cancellationToken)
    {
        var credential = access.Read(PublicCheckoutId);
        if (credential is null)
        {
            State = CheckoutLoadState.NotFound;
            Checkout = null;
            Message = "Esta reserva não está disponível neste dispositivo.";
            return null;
        }

        var result = await client.GetAsync(PublicCheckoutId, credential.Token, cancellationToken);
        State = result.State;
        Checkout = result.Checkout;
        Message = result.Message ?? (result.State == CheckoutLoadState.NotFound ? "Esta reserva não está disponível ou já expirou." : null);
        if (result.State == CheckoutLoadState.NotFound)
        {
            access.Clear();
        }
        if (Checkout is not null)
        {
            HasCurrentCart = cart.Read().Lines.Count > 0;
            await LoadPaymentMethodsAsync(cancellationToken);
            var payment = await client.GetPaymentAsync(PublicCheckoutId, credential.Token, cancellationToken);
            PaymentState = payment.State; Payment = payment.Payment;
            if (Payment is not null)
            {
                PaymentMethod = Payment.Method;
            }
            else
            {
                PaymentMethod = AvailableOrDefault(PaymentMethod);
            }
            Message = PaymentLifecycleMessage(Payment);
            if (Payment is { PublicOrderNumber: { } number } && Payment.Status == "converted" && orderAccess.Write(number, credential.Token))
            {
                return RedirectToPage("/Order", new { publicOrderNumber = number });
            }
        }
        return null;
    }

    private async Task LoadPaymentMethodsAsync(CancellationToken cancellationToken)
    {
        var configuration = await client.GetConfigurationAsync(cancellationToken);
        OnlinePaymentMethods = configuration.State == CheckoutLoadState.Success
            ? StorefrontOnlinePayments.Normalize(configuration.Configuration?.OnlinePaymentMethods)
            : [OnlinePaymentMethod.Pix];
    }

    private OnlinePaymentMethod AvailableOrDefault(OnlinePaymentMethod method)
    {
        if (OnlinePaymentMethods.Contains(method))
        {
            return method;
        }

        return StorefrontOnlinePayments.DefaultMethod(OnlinePaymentMethods) ?? OnlinePaymentMethod.Pix;
    }

    private async Task<IActionResult> RefreshPaymentFlowAsync(CancellationToken cancellationToken)
    {
        var loaded = await LoadOwnedAsync(cancellationToken);
        if (loaded is RedirectToPageResult && Payment?.PublicOrderNumber is { } number)
        {
            return new JsonResult(new { redirectUrl = Url.Page("/Order", new { publicOrderNumber = number }) });
        }

        return PaymentFlowFragment();
    }

    private IActionResult PaymentFlowFragment() => Partial("_CheckoutPaymentFlow", this);
    private IActionResult Inaccessible() { State = CheckoutLoadState.NotFound; Checkout = null; Message = "Esta reserva não está disponível neste dispositivo."; return IsAjaxPaymentRequest ? PaymentFlowFragment() : Page(); }
    private static string? PaymentLifecycleMessage(PixPayment? payment) => payment?.Status == "cancellationpending" ? "Estamos confirmando o cancelamento do pagamento. Aguarde a atualização; não é necessário tentar cancelar novamente." : null;
    private static string PaymentMessage(PaymentLoadState state, OnlinePaymentMethod? method = null) => state switch
    {
        PaymentLoadState.Validation => method == OnlinePaymentMethod.Card
            ? "Não foi possível iniciar o pagamento com cartão com os dados atuais."
            : "Não foi possível iniciar o pagamento PIX com os dados atuais.",
        PaymentLoadState.Conflict => method == OnlinePaymentMethod.Card
            ? "A tentativa de pagamento com cartão mudou. Atualize a página e tente novamente."
            : "A tentativa de pagamento PIX mudou. Atualize a página e tente novamente.",
        PaymentLoadState.Timeout => "A confirmação do pagamento demorou. Tente novamente.",
        PaymentLoadState.Unavailable => "O pagamento está temporariamente indisponível.",
        PaymentLoadState.Malformed => "Não foi possível validar os dados do pagamento.",
        PaymentLoadState.RateLimited => "Muitas tentativas. Aguarde um pouco.",
        _ => "O pagamento não pôde ser iniciado agora."
    };
}
