using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class CheckoutModel(
    ICartCookieStore cart,
    ICatalogClient catalog,
    ICheckoutClient checkout,
    ICheckoutDraftCookieStore draft,
    ICheckoutAccessCookieStore access,
    CheckoutRateLimiter rateLimiter) : PageModel
{
    public CartState Cart { get; private set; } = new(DateTimeOffset.UtcNow, []);
    public CatalogQuoteResult Quote { get; private set; } = CatalogQuoteResult.Unavailable();
    public CheckoutConfigurationResult Configuration { get; private set; } = CheckoutConfigurationResult.Failure(CheckoutLoadState.Unavailable);
    public CheckoutLoadState? ErrorState { get; private set; }
    public string? ErrorMessage { get; private set; }
    [BindProperty] public ContactInput Contact { get; set; } = new();
    public bool Empty => Cart.Lines.Count == 0;
    public bool CanSubmit => !Empty && Quote.State == CatalogLoadState.Success && Configuration.State == CheckoutLoadState.Success && Configuration.Configuration?.PickupEnabled == true;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        await LoadAsync(cancellationToken);
        draft.Ensure();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        Cart = cart.Read();
        await LoadConfigurationAsync(cancellationToken);
        if (Empty) { ErrorState = CheckoutLoadState.Validation; ErrorMessage = "Seu carrinho está vazio."; return Page(); }
        if (!rateLimiter.TryConsume(ClientIdentityResolver.Resolve(HttpContext, HttpContext.RequestServices.GetRequiredService<IHostEnvironment>()), "checkout-create")) { ErrorState = CheckoutLoadState.RateLimited; ErrorMessage = "Muitas tentativas. Aguarde um pouco."; await LoadQuoteAsync(cancellationToken); return Page(); }
        await LoadQuoteAsync(cancellationToken);
        if (!ModelState.IsValid || !CanSubmit) { ErrorState = !CanSubmit && Quote.State == CatalogLoadState.Unavailable ? CheckoutLoadState.Unavailable : CheckoutLoadState.Validation; ErrorMessage ??= CanSubmit ? "Confira os dados informados." : "Os itens do carrinho não estão disponíveis para reserva."; return Page(); }
        var credentials = draft.Ensure();
        var result = await checkout.CreateAsync(new(Cart.Lines, new CheckoutContact { Name = Contact.Name.Trim(), Email = Contact.Email.Trim(), Phone = Contact.Phone.Trim() }, Configuration.Configuration!.PublicPickupId!.Value), credentials.IdempotencyKey, credentials.AccessToken, cancellationToken);
        if (result.State == CheckoutLoadState.Success && result.Checkout is not null)
        {
            if (!access.Write(result.Checkout, credentials.AccessToken))
            {
                ErrorState = CheckoutLoadState.Malformed;
                ErrorMessage = "Não foi possível proteger o acesso à reserva. Seus itens foram preservados.";
                return Page();
            }

            draft.Clear();
            cart.Clear();
            return RedirectToPage("/CheckoutStatus", new { publicCheckoutId = result.Checkout.PublicCheckoutId });
        }
        ErrorState = result.State; ErrorMessage = result.Message ?? Message(result.State);
        if (result.State is CheckoutLoadState.Validation or CheckoutLoadState.Conflict) { draft.Clear(); draft.Ensure(); }
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken) { Cart = cart.Read(); await LoadConfigurationAsync(cancellationToken); await LoadQuoteAsync(cancellationToken); }
    private async Task LoadConfigurationAsync(CancellationToken cancellationToken) { Configuration = await checkout.GetConfigurationAsync(cancellationToken); if (Configuration.State != CheckoutLoadState.Success) { ErrorState = Configuration.State; ErrorMessage = Message(Configuration.State); } }
    private async Task LoadQuoteAsync(CancellationToken cancellationToken)
    {
        if (Empty) return;
        Quote = await catalog.QuoteAsync(new(Cart.Lines.Select(x => new CatalogQuoteItem(x.PublicOfferId, x.Quantity)).ToList()), cancellationToken);
        if (Quote.State == CatalogLoadState.Unavailable)
        {
            ErrorState = CheckoutLoadState.Unavailable;
            ErrorMessage = "Não foi possível consultar os preços atuais. Seus itens foram preservados.";
        }
    }
    private static string Message(CheckoutLoadState state) => state switch { CheckoutLoadState.Timeout => "A confirmação demorou. Seus dados foram preservados; tente novamente.", CheckoutLoadState.Unavailable or CheckoutLoadState.Malformed => "Não foi possível acessar o serviço agora. Tente novamente.", CheckoutLoadState.RateLimited => "Muitas tentativas. Aguarde um pouco.", CheckoutLoadState.Conflict => "A tentativa anterior não corresponde ao carrinho atual. Tente novamente.", _ => "Não foi possível criar a reserva com os dados atuais." };

    public sealed class ContactInput
    {
        [Required(ErrorMessage = "Informe seu nome.")]
        [StringLength(120, MinimumLength = 2, ErrorMessage = "Informe seu nome completo.")]
        public string Name { get; set; } = "";
        [Required(ErrorMessage = "Informe seu e-mail.")]
        [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
        [StringLength(254)]
        public string Email { get; set; } = "";
        [Required(ErrorMessage = "Informe seu telefone.")]
        [StringLength(40, MinimumLength = 8, ErrorMessage = "Informe um telefone válido.")]
        public string Phone { get; set; } = "";
    }
}
