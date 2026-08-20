using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class CheckoutStatusModel(ICheckoutClient client, ICheckoutAccessCookieStore access, IPaymentAttemptCookieStore paymentAttempt, IOrderAccessCookieStore orderAccess, CheckoutRateLimiter rateLimiter) : PageModel
{
    public CheckoutResponse? Checkout { get; private set; }
    public CheckoutLoadState State { get; private set; } = CheckoutLoadState.NotFound;
    public string? Message { get; private set; }
    public PixPayment? Payment { get; private set; }
    public PaymentLoadState PaymentState { get; private set; } = PaymentLoadState.NotFound;
    [BindProperty(SupportsGet = true)] public Guid PublicCheckoutId { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        return await LoadOwnedAsync(cancellationToken) ?? Page();
    }

    public async Task<IActionResult> OnPostPayPixAsync(CancellationToken cancellationToken)
    {
        var credential = access.Read(PublicCheckoutId);
        if (credential is null) return Inaccessible();
        var result = await client.InitiatePixAsync(PublicCheckoutId, credential.Token, paymentAttempt.Ensure(PublicCheckoutId).IdempotencyKey, cancellationToken);
        if (result.State == PaymentLoadState.Success && result.Payment is { PublicOrderNumber: { } number } && result.Payment.Status == "converted")
        {
            if (orderAccess.Write(number, credential.Token)) return RedirectToPage("/Order", new { publicOrderNumber = number });
            return Inaccessible();
        }
        await LoadOwnedAsync(cancellationToken);
        PaymentState = result.State; Payment = result.Payment; Message = result.Message ?? PaymentMessage(result.State);
        return Page();
    }

    public async Task<IActionResult> OnGetPaymentAsync(CancellationToken cancellationToken)
    {
        var credential = access.Read(PublicCheckoutId);
        if (credential is null) return new JsonResult(new { state = "inaccessible" }) { StatusCode = StatusCodes.Status404NotFound };
        var result = await client.GetPaymentAsync(PublicCheckoutId, credential.Token, cancellationToken);
        if (result.State == PaymentLoadState.Success && result.Payment is { PublicOrderNumber: { } number } && result.Payment.Status == "converted" && orderAccess.Write(number, credential.Token)) return new JsonResult(new { state = "converted", url = Url.Page("/Order", new { publicOrderNumber = number }) });
        return new JsonResult(new { state = result.State.ToString().ToLowerInvariant(), status = result.Payment?.Status, expiresAt = result.Payment?.ExpiresAt });
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken cancellationToken)
    {
        var credential = access.Read(PublicCheckoutId);
        if (credential is null) { State = CheckoutLoadState.NotFound; Message = "Esta reserva não está disponível neste dispositivo."; return Page(); }
        if (!rateLimiter.TryConsume(ClientIdentityResolver.Resolve(HttpContext, HttpContext.RequestServices.GetRequiredService<IHostEnvironment>()), "checkout-cancel"))
        {
            await LoadOwnedAsync(cancellationToken);
            State = CheckoutLoadState.RateLimited;
            Message = "Muitas tentativas. Aguarde um pouco.";
            return Page();
        }
        var currentPayment = await client.GetPaymentAsync(PublicCheckoutId, credential.Token, cancellationToken);
        if (currentPayment.State == PaymentLoadState.Success && currentPayment.Payment is { Status: "pending" or "processing" or "approved" or "conversionpending" })
        {
            var paymentCancellation = await client.CancelPaymentAsync(PublicCheckoutId, credential.Token, cancellationToken);
            if (paymentCancellation.State == PaymentLoadState.Success) return RedirectToPage(new { publicCheckoutId = PublicCheckoutId });
            await LoadOwnedAsync(cancellationToken); PaymentState = paymentCancellation.State; Payment = paymentCancellation.Payment; Message = "O pagamento não pode ser cancelado neste momento."; return Page();
        }
        if (currentPayment.State != PaymentLoadState.NotFound)
        {
            await LoadOwnedAsync(cancellationToken);
            PaymentState = currentPayment.State;
            Message = "O pagamento não pode ser cancelado neste momento.";
            return Page();
        }
        var result = await client.CancelAsync(PublicCheckoutId, credential.Token, cancellationToken);
        if (result.State == CheckoutLoadState.Success) return RedirectToPage(new { publicCheckoutId = PublicCheckoutId });
        if (result.State == CheckoutLoadState.NotFound)
        {
            access.Clear();
            State = result.State;
            Checkout = null;
            Message = "Esta reserva não está disponível ou já expirou.";
            return Page();
        }

        var errorMessage = result.Message ?? "A reserva não pode ser cancelada neste momento.";
        await LoadOwnedAsync(cancellationToken);
        if (Checkout is not null)
        {
            State = result.State;
            Message = errorMessage;
        }
        return Page();
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
            var payment = await client.GetPaymentAsync(PublicCheckoutId, credential.Token, cancellationToken);
            PaymentState = payment.State; Payment = payment.Payment;
            if (Payment is { PublicOrderNumber: { } number } && Payment.Status == "converted" && orderAccess.Write(number, credential.Token))
            {
                return RedirectToPage("/Order", new { publicOrderNumber = number });
            }
        }
        return null;
    }

    private IActionResult Inaccessible() { State = CheckoutLoadState.NotFound; Checkout = null; Message = "Esta reserva não está disponível neste dispositivo."; return Page(); }
    private static string PaymentMessage(PaymentLoadState state) => state switch { PaymentLoadState.Timeout => "A confirmação do pagamento demorou. Tente novamente.", PaymentLoadState.Unavailable => "O pagamento está temporariamente indisponível.", PaymentLoadState.Malformed => "Não foi possível validar os dados do pagamento.", PaymentLoadState.RateLimited => "Muitas tentativas. Aguarde um pouco.", _ => "O pagamento não pôde ser iniciado agora." };
}
