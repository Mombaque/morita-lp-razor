using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;

namespace Morita.LP.Razor.Pages;

public sealed class AccountModel(ICustomerAccountClient client, ICustomerAccountCookieStore cookies, IOptions<StorefrontOptions>? storefrontOptions = null) : PageModel
{
    public CustomerAccountProfile? Profile { get; private set; }
    public IReadOnlyList<PublicOrder> Orders { get; private set; } = [];
    public PublicOrder? SelectedOrder { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public string? OrdersError { get; private set; }
    public bool OrdersLoaded { get; private set; }
    public string? PrivacyPolicyUrl => storefrontOptions?.Value.PrivacyPolicyUrl;
    public bool AccountEnabled => storefrontOptions?.Value.CustomerAccountsEnabled ?? true;
    public bool ChallengeIssued => ChallengeId != Guid.Empty;
    public bool SignedIn => Profile is not null;
    [BindProperty] public EmailInput EmailForm { get; set; } = new();
    [BindProperty] public VerificationInput Verification { get; set; } = new();
    [BindProperty] public ProfileInput ProfileForm { get; set; } = new();
    [BindProperty] public EmailChangeInput EmailChange { get; set; } = new();
    [BindProperty] public Guid ChallengeId { get; set; }
    [BindProperty] public DateTimeOffset? ChallengeExpiresAt { get; set; }
    [BindProperty] public string? PrivacyPolicyVersion { get; set; }
    [BindProperty] public bool AcceptedPrivacyPolicy { get; set; }
    [BindProperty] public bool ConfirmClosure { get; set; }
    [BindProperty(SupportsGet = true)] public string? PublicOrderNumber { get; set; }
    private CustomerAccountSession? Session => cookies.Read();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ViewData["Robots"] = "noindex,nofollow";
        await LoadAsync(ct);
        return Page();
    }
    public async Task<IActionResult> OnPostRequestCodeAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear(); TryValidateModel(EmailForm, nameof(EmailForm));
        if (!ModelState.IsValid) return Page();
        var result = await client.RequestCodeAsync(EmailForm?.Email?.Trim() ?? "", ct);
        if (result.State != AccountLoadState.Success || result.Value is null) { Error = result.Message ?? "Não foi possível enviar o código."; return Page(); }
        ChallengeId = result.Value.ChallengeId; ChallengeExpiresAt = result.Value.ExpiresAt; PrivacyPolicyVersion = result.Value.PrivacyPolicyVersion;
        Message = "Enviamos um código para seu e-mail. Ele expira em poucos minutos."; return Page();
    }
    public async Task<IActionResult> OnPostVerifyCodeAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear(); TryValidateModel(Verification, nameof(Verification));
        ChallengeId = Verification.ChallengeId;
        if (Verification.ChallengeId == Guid.Empty || !ChallengeIssued) ModelState.AddModelError(nameof(Verification.Code), "Solicite um código antes de confirmar a entrada.");
        if (string.IsNullOrWhiteSpace(PrivacyPolicyVersion)) ModelState.AddModelError(nameof(PrivacyPolicyVersion), "Não foi possível confirmar a versão da política de privacidade. Solicite um novo código.");
        if (!AcceptedPrivacyPolicy) ModelState.AddModelError(nameof(AcceptedPrivacyPolicy), "Aceite a política de privacidade para entrar na conta.");
        if (!ModelState.IsValid) return Page();
        var result = await client.VerifyCodeAsync(Verification.ChallengeId, Verification?.Code?.Trim() ?? "", AcceptedPrivacyPolicy, PrivacyPolicyVersion!, ct);
        if (result.State != AccountLoadState.Success || result.Value.Session is null || result.Value.Session.Token.Length == 0) { Error = result.Message ?? "Código inválido ou expirado."; return Page(); }
        if (!cookies.Write(result.Value.Session.Token, result.Value.Session.ExpiresAt)) { Error = "Não foi possível proteger sua sessão."; return Page(); }
        return RedirectToPage("/Account");
    }
    public async Task<IActionResult> OnPostSaveProfileAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear(); TryValidateModel(ProfileForm, nameof(ProfileForm));
        ValidateAddress();
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.UpdateProfileAsync(session.Token, Clean(ProfileForm?.Name), Clean(ProfileForm?.Phone), ProfileForm?.Address?.ToModel(), ct);
        if (!result.Value) { ExpireIfNeeded(result.State); Error = result.Message ?? "Não foi possível salvar seus dados."; }
        else Message = "Dados salvos.";
        await LoadAsync(ct); return Page();
    }
    public async Task<IActionResult> OnPostRequestEmailCodeAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear(); TryValidateModel(EmailChange, nameof(EmailChange));
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.RequestEmailCodeAsync(session.Token, EmailChange?.Email?.Trim() ?? "", ct);
        if (result.State == AccountLoadState.Success && result.Value is { } challenge) { ChallengeId = challenge.ChallengeId; Message = "Enviamos o código para o novo e-mail."; } else { ExpireIfNeeded(result.State); Error = result.Message ?? "Não foi possível alterar o e-mail."; }
        await LoadAsync(ct); return Page();
    }
    public async Task<IActionResult> OnPostVerifyEmailCodeAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear(); TryValidateModel(Verification, nameof(Verification));
        if (!ModelState.IsValid || Verification.ChallengeId == Guid.Empty) { await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.VerifyEmailCodeAsync(session.Token, Verification.ChallengeId, Verification.Code.Trim(), ct);
        if (result.Value) Message = "E-mail atualizado. As outras sessões foram encerradas."; else { ExpireIfNeeded(result.State); Error = result.Message ?? "Código inválido."; }
        await LoadAsync(ct); return Page();
    }
    public async Task<IActionResult> OnPostLogoutAsync(bool all, CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        if (Session is { } session) await client.LogoutAsync(session.Token, all, ct);
        cookies.Clear(); return RedirectToPage("/Account");
    }
    public async Task<IActionResult> OnPostRequestClosureCodeAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear();
        if (!ConfirmClosure) { ModelState.AddModelError(nameof(ConfirmClosure), "Marque a confirmação para encerrar a conta."); await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.RequestClosureCodeAsync(session.Token, ct); if (result.State == AccountLoadState.Success && result.Value is { } challenge) { ChallengeId = challenge.ChallengeId; Message = "Enviamos um código para confirmar o encerramento."; } else { ExpireIfNeeded(result.State); Error = result.Message ?? "Não foi possível iniciar o encerramento."; } await LoadAsync(ct); return Page();
    }
    public async Task<IActionResult> OnPostVerifyClosureCodeAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear();
        TryValidateModel(Verification, nameof(Verification));
        if (!ConfirmClosure) ModelState.AddModelError(nameof(ConfirmClosure), "Marque a confirmação para encerrar a conta.");
        if (!ModelState.IsValid || Verification.ChallengeId == Guid.Empty) { await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.VerifyClosureCodeAsync(session.Token, Verification.ChallengeId, Verification.Code.Trim(), ct); if (result.Value) { cookies.Clear(); return RedirectToPage("/Account"); } ExpireIfNeeded(result.State); Error = result.Message ?? "Código inválido."; await LoadAsync(ct); return Page();
    }
    private async Task LoadAsync(CancellationToken ct)
    {
        if (Session is not { } session) return;
        var profile = await client.GetProfileAsync(session.Token, ct);
        if (profile.State == AccountLoadState.Success && profile.Value is { } loadedProfile) { Profile = loadedProfile; ProfileForm = ProfileInput.From(loadedProfile); }
        else { ExpireIfNeeded(profile.State); Error = profile.Message; return; }
        var orders = await client.GetOrdersAsync(session.Token, ct); OrdersLoaded = true;
        if (orders.State == AccountLoadState.Success && orders.Value is not null) Orders = orders.Value;
        else if (orders.State == AccountLoadState.Unauthorized) { cookies.Clear(); Profile = null; Error = orders.Message ?? "Sua sessão expirou. Entre novamente."; return; }
        else OrdersError = orders.Message ?? "Não foi possível carregar seu histórico agora. Tente novamente em instantes.";
        if (!string.IsNullOrWhiteSpace(PublicOrderNumber))
        {
            var order = await client.GetOrderAsync(session.Token, PublicOrderNumber, ct);
            if (order.State == AccountLoadState.Unauthorized) { cookies.Clear(); Profile = null; Error = order.Message ?? "Sua sessão expirou. Entre novamente."; return; }
            SelectedOrder = order.Value;
        }
    }
    private void ExpireIfNeeded(AccountLoadState state) { if (state == AccountLoadState.Unauthorized) cookies.Clear(); }
    private void ValidateAddress()
    {
        var address = ProfileForm.Address;
        if (!address.HasAnyValue) return;
        Required(address.Recipient, "ProfileForm.Address.Recipient", "Informe o destinatário.");
        Required(address.Street, "ProfileForm.Address.Street", "Informe a rua.");
        Required(address.Number, "ProfileForm.Address.Number", "Informe o número.");
        Required(address.Neighborhood, "ProfileForm.Address.Neighborhood", "Informe o bairro.");
        Required(address.City, "ProfileForm.Address.City", "Informe a cidade.");
        if (!BrazilianStates.Contains(Clean(address.State).ToUpperInvariant())) ModelState.AddModelError("ProfileForm.Address.State", "Informe uma UF brasileira válida.");
        if (!ValidPostalCode(address.PostalCode)) ModelState.AddModelError("ProfileForm.Address.PostalCode", "Informe um CEP brasileiro válido.");
    }
    private void Required(string value, string key, string message) { if (string.IsNullOrWhiteSpace(value)) ModelState.AddModelError(key, message); }
    private static bool ValidPostalCode(string? value) => value is not null && value.Count(char.IsAsciiDigit) == 8 && value.All(character => char.IsAsciiDigit(character) || character is '-' or ' ' or '.');
    private static readonly HashSet<string> BrazilianStates = ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];
    private static string Clean(string? value) => value?.Trim() ?? "";
    public sealed class EmailInput { [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = ""; }
    public sealed class VerificationInput { public Guid ChallengeId { get; set; } [Required, StringLength(6, MinimumLength = 6)] public string Code { get; set; } = ""; }
    public sealed class EmailChangeInput { [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = ""; }
    public sealed class ProfileInput
    {
        [StringLength(120)] public string Name { get; set; } = "";
        [StringLength(40)] public string Phone { get; set; } = "";
        public AddressInput Address { get; set; } = new();
        public CustomerAccountAddress? ToModel() => Address.ToModel();
        public static ProfileInput From(CustomerAccountProfile p) => new() { Name = p.Name ?? "", Phone = p.Phone ?? "", Address = p.Address is null ? new() : new() { Recipient = p.Address.Recipient, Street = p.Address.Street, Number = p.Address.Number, Complement = p.Address.Complement ?? "", Neighborhood = p.Address.Neighborhood, City = p.Address.City, State = p.Address.State, PostalCode = p.Address.PostalCode } };
    }
    public sealed class AddressInput
    {
        [StringLength(120)] public string Recipient { get; set; } = "";
        [StringLength(160)] public string Street { get; set; } = "";
        [StringLength(40)] public string Number { get; set; } = "";
        [StringLength(160)] public string Complement { get; set; } = "";
        [StringLength(120)] public string Neighborhood { get; set; } = "";
        [StringLength(120)] public string City { get; set; } = "";
        [StringLength(2)] public string State { get; set; } = "";
        [StringLength(10)] public string PostalCode { get; set; } = "";
        public bool HasAnyValue => new[] { Recipient, Street, Number, Complement, Neighborhood, City, State, PostalCode }.Any(value => !string.IsNullOrWhiteSpace(value));
        public CustomerAccountAddress? ToModel() => string.IsNullOrWhiteSpace(Street) ? null : new() { Recipient = Clean(Recipient), Street = Clean(Street), Number = Clean(Number), Complement = string.IsNullOrWhiteSpace(Complement) ? null : Complement.Trim(), Neighborhood = Clean(Neighborhood), City = Clean(City), State = Clean(State).ToUpperInvariant(), PostalCode = Clean(PostalCode), CountryCode = "BR" };
    }
}
