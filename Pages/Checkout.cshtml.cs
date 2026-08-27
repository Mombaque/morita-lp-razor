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
    public ShippingQuoteResult ShippingQuotes { get; private set; } = ShippingQuoteResult.Failure(CheckoutLoadState.Validation);
    [BindProperty] public ContactInput Contact { get; set; } = new();
    [BindProperty] public string FulfillmentMethod { get; set; } = "pickup";
    [BindProperty] public Guid? PublicShippingQuoteId { get; set; }
    [BindProperty] public ShippingAddressInput ShippingAddress { get; set; } = new();
    public bool Empty => Cart.Lines.Count == 0;
    public bool CanSubmit => !Empty && Quote.State == CatalogLoadState.Success && Configuration.State == CheckoutLoadState.Success &&
        (FulfillmentMethod == "pickup" && Configuration.Configuration?.PickupEnabled == true ||
         FulfillmentMethod == "shipping" && Configuration.Configuration?.ShippingEnabled == true && PublicShippingQuoteId.HasValue);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        await LoadAsync(cancellationToken);
        FulfillmentMethod = Configuration.Configuration?.PickupEnabled == true ? "pickup" : "shipping";
        draft.Ensure();
    }

    public async Task<IActionResult> OnPostQuoteShippingAsync(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        ModelState.Clear();
        FulfillmentMethod = "shipping";
        PublicShippingQuoteId = null;
        Cart = cart.Read();
        await LoadConfigurationAsync(cancellationToken);
        await LoadQuoteAsync(cancellationToken);
        if (Empty) { ErrorState = CheckoutLoadState.Validation; ErrorMessage = "Seu carrinho está vazio."; return Page(); }
        if (Configuration.Configuration?.ShippingEnabled != true) { ErrorState = CheckoutLoadState.Unavailable; ErrorMessage = "A entrega está temporariamente indisponível."; return Page(); }
        if (!ValidPostalCode(ShippingAddress.PostalCode)) { ErrorState = CheckoutLoadState.Validation; ErrorMessage = "Informe um CEP brasileiro válido para calcular o frete."; return Page(); }
        if (!rateLimiter.TryConsume(ClientIdentityResolver.Resolve(HttpContext, HttpContext.RequestServices.GetRequiredService<IHostEnvironment>()), "checkout-shipping-quote")) { ErrorState = CheckoutLoadState.RateLimited; ErrorMessage = "Muitas consultas de frete. Aguarde um pouco."; return Page(); }
        await LoadShippingQuotesAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["Robots"] = "noindex,nofollow";
        Cart = cart.Read();
        await LoadConfigurationAsync(cancellationToken);
        if (Empty) { ErrorState = CheckoutLoadState.Validation; ErrorMessage = "Seu carrinho está vazio."; return Page(); }
        if (!rateLimiter.TryConsume(ClientIdentityResolver.Resolve(HttpContext, HttpContext.RequestServices.GetRequiredService<IHostEnvironment>()), "checkout-create")) { ErrorState = CheckoutLoadState.RateLimited; ErrorMessage = "Muitas tentativas. Aguarde um pouco."; await LoadQuoteAsync(cancellationToken); return Page(); }
        await LoadQuoteAsync(cancellationToken);
        ValidateFulfillment();
        if (!ModelState.IsValid || !CanSubmit) { ErrorState = !CanSubmit && Quote.State == CatalogLoadState.Unavailable ? CheckoutLoadState.Unavailable : CheckoutLoadState.Validation; ErrorMessage ??= !ModelState.IsValid ? "Confira os dados informados." : FulfillmentMethod == "shipping" ? "Calcule o frete e escolha uma opção de entrega." : "Os itens do carrinho não estão disponíveis para reserva."; if (FulfillmentMethod == "shipping") await LoadShippingQuotesAsync(cancellationToken); return Page(); }
        var credentials = draft.Ensure();
        var fulfillment = FulfillmentMethod == "pickup"
            ? new CheckoutFulfillment("pickup", Configuration.Configuration!.PublicPickupId)
            : new CheckoutFulfillment("shipping", PublicShippingQuoteId: PublicShippingQuoteId, ShippingAddress: new CheckoutAddress
            {
                Recipient = Clean(ShippingAddress.Recipient), Street = Clean(ShippingAddress.Street), Number = Clean(ShippingAddress.Number),
                Complement = string.IsNullOrWhiteSpace(ShippingAddress.Complement) ? null : ShippingAddress.Complement.Trim(), Neighborhood = Clean(ShippingAddress.Neighborhood),
                City = Clean(ShippingAddress.City), State = Clean(ShippingAddress.State).ToUpperInvariant(), PostalCode = Clean(ShippingAddress.PostalCode), CountryCode = "BR"
            });
        var result = await checkout.CreateAsync(new(Cart.Lines, new CheckoutContact { Name = Contact.Name.Trim(), Email = Contact.Email.Trim(), Phone = Contact.Phone.Trim() }, fulfillment), credentials.IdempotencyKey, credentials.AccessToken, cancellationToken);
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
        if (result.State is CheckoutLoadState.Validation or CheckoutLoadState.Conflict)
        {
            draft.Clear(); draft.Ensure();
            if (FulfillmentMethod == "shipping") { PublicShippingQuoteId = null; await LoadShippingQuotesAsync(cancellationToken); }
        }
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
    private async Task LoadShippingQuotesAsync(CancellationToken cancellationToken)
    {
        if (Empty || !ValidPostalCode(ShippingAddress.PostalCode)) return;
        ShippingQuotes = await checkout.QuoteShippingAsync(new ShippingQuoteRequest(Cart.Lines, ShippingAddress.PostalCode), cancellationToken);
        if (ShippingQuotes.State != CheckoutLoadState.Success)
        {
            ErrorState = ShippingQuotes.State;
            ErrorMessage = ShippingQuotes.Message ?? "Não foi possível calcular o frete para este CEP.";
        }
    }
    private void ValidateFulfillment()
    {
        if (FulfillmentMethod == "pickup")
        {
            if (Configuration.Configuration?.PickupEnabled != true || Configuration.Configuration.PublicPickupId is null)
                ModelState.AddModelError(nameof(FulfillmentMethod), "A retirada na loja está temporariamente indisponível.");
            return;
        }
        if (FulfillmentMethod != "shipping" || Configuration.Configuration?.ShippingEnabled != true)
        {
            ModelState.AddModelError(nameof(FulfillmentMethod), "Escolha uma forma de entrega disponível.");
            return;
        }
        Required(ShippingAddress.Recipient, "ShippingAddress.Recipient", "Informe o nome de quem receberá o pedido.");
        Required(ShippingAddress.Street, "ShippingAddress.Street", "Informe o endereço de entrega.");
        Required(ShippingAddress.Number, "ShippingAddress.Number", "Informe o número do endereço.");
        Required(ShippingAddress.Neighborhood, "ShippingAddress.Neighborhood", "Informe o bairro.");
        Required(ShippingAddress.City, "ShippingAddress.City", "Informe a cidade.");
        if (!BrazilianStates.Contains(Clean(ShippingAddress.State).ToUpperInvariant())) ModelState.AddModelError("ShippingAddress.State", "Informe uma UF brasileira válida.");
        if (!ValidPostalCode(ShippingAddress.PostalCode)) ModelState.AddModelError("ShippingAddress.PostalCode", "Informe um CEP brasileiro válido.");
        if (!PublicShippingQuoteId.HasValue || PublicShippingQuoteId == Guid.Empty) ModelState.AddModelError(nameof(PublicShippingQuoteId), "Calcule o frete e escolha uma opção de entrega.");
    }
    private void Required(string value, string key, string message) { if (string.IsNullOrWhiteSpace(value)) ModelState.AddModelError(key, message); }
    private static bool ValidPostalCode(string? value) => value is not null && value.Count(char.IsAsciiDigit) == 8 && value.All(character => char.IsAsciiDigit(character) || character is '-' or ' ' or '.');
    private static string Clean(string? value) => value?.Trim() ?? "";
    private static readonly HashSet<string> BrazilianStates = ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];
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

    public sealed class ShippingAddressInput
    {
        [StringLength(120)] public string Recipient { get; set; } = "";
        [StringLength(160)] public string Street { get; set; } = "";
        [StringLength(30)] public string Number { get; set; } = "";
        [StringLength(120)] public string Complement { get; set; } = "";
        [StringLength(120)] public string Neighborhood { get; set; } = "";
        [StringLength(120)] public string City { get; set; } = "";
        [StringLength(2)] public string State { get; set; } = "";
        [StringLength(10)] public string PostalCode { get; set; } = "";
    }
}
