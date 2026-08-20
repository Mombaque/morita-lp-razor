using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class CheckoutStatusModel(ICheckoutClient client, ICheckoutAccessCookieStore access, CheckoutRateLimiter rateLimiter) : PageModel
{
    public CheckoutResponse? Checkout { get; private set; }
    public CheckoutLoadState State { get; private set; } = CheckoutLoadState.NotFound;
    public string? Message { get; private set; }
    [BindProperty(SupportsGet = true)] public Guid PublicCheckoutId { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        await LoadOwnedAsync(cancellationToken);
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

    private async Task LoadOwnedAsync(CancellationToken cancellationToken)
    {
        var credential = access.Read(PublicCheckoutId);
        if (credential is null)
        {
            State = CheckoutLoadState.NotFound;
            Checkout = null;
            Message = "Esta reserva não está disponível neste dispositivo.";
            return;
        }

        var result = await client.GetAsync(PublicCheckoutId, credential.Token, cancellationToken);
        State = result.State;
        Checkout = result.Checkout;
        Message = result.Message ?? (result.State == CheckoutLoadState.NotFound ? "Esta reserva não está disponível ou já expirou." : null);
        if (result.State == CheckoutLoadState.NotFound)
        {
            access.Clear();
        }
    }
}
