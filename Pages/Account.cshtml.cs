using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class AccountModel(ICustomerAccountClient client, ICustomerAccountCookieStore cookies) : PageModel
{
    public CustomerAccountProfile? Profile { get; private set; }
    public IReadOnlyList<PublicOrder> Orders { get; private set; } = [];
    public PublicOrder? SelectedOrder { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public bool SignedIn => Profile is not null;
    [BindProperty] public EmailInput EmailForm { get; set; } = new();
    [BindProperty] public VerificationInput Verification { get; set; } = new();
    [BindProperty] public ProfileInput ProfileForm { get; set; } = new();
    [BindProperty] public EmailChangeInput EmailChange { get; set; } = new();
    [BindProperty] public Guid ChallengeId { get; set; }
    [BindProperty] public bool AcceptedPrivacyPolicy { get; set; }
    [BindProperty] public bool ConfirmClosure { get; set; }
    [BindProperty(SupportsGet = true)] public string? PublicOrderNumber { get; set; }
    private CustomerAccountSession? Session => cookies.Read();

    public async Task OnGetAsync(CancellationToken ct)
    {
        ViewData["Robots"] = "noindex,nofollow";
        await LoadAsync(ct);
    }
    public async Task<IActionResult> OnPostRequestCodeAsync(CancellationToken ct)
    {
        ModelState.Clear(); TryValidateModel(EmailForm, nameof(EmailForm));
        if (!ModelState.IsValid) return Page();
        var result = await client.RequestCodeAsync(EmailForm?.Email?.Trim() ?? "", ct);
        if (result.State != AccountLoadState.Success || result.Value is null) { Error = result.Message ?? "Não foi possível enviar o código."; return Page(); }
        ChallengeId = result.Value.ChallengeId; Message = "Enviamos um código para seu e-mail. Ele expira em poucos minutos."; return Page();
    }
    public async Task<IActionResult> OnPostVerifyCodeAsync(CancellationToken ct)
    {
        ModelState.Clear(); TryValidateModel(Verification, nameof(Verification));
        if (!ModelState.IsValid || Verification.ChallengeId == Guid.Empty) return Page();
        var result = await client.VerifyCodeAsync(Verification.ChallengeId, Verification?.Code?.Trim() ?? "", AcceptedPrivacyPolicy, "customer-account-v1", ct);
        if (result.State != AccountLoadState.Success || result.Value.Session is null || result.Value.Session.Token.Length == 0) { Error = result.Message ?? "Código inválido ou expirado."; return Page(); }
        if (!cookies.Write(result.Value.Session.Token, result.Value.Session.ExpiresAt)) { Error = "Não foi possível proteger sua sessão."; return Page(); }
        return RedirectToPage("/Account");
    }
    public async Task<IActionResult> OnPostSaveProfileAsync(CancellationToken ct)
    {
        ModelState.Clear(); TryValidateModel(ProfileForm, nameof(ProfileForm));
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.UpdateProfileAsync(session.Token, Clean(ProfileForm?.Name), Clean(ProfileForm?.Phone), ProfileForm?.Address?.ToModel(), ct);
        if (!result.Value) { ExpireIfNeeded(result.State); Error = result.Message ?? "Não foi possível salvar seus dados."; }
        else Message = "Dados salvos.";
        await LoadAsync(ct); return Page();
    }
    public async Task<IActionResult> OnPostRequestEmailCodeAsync(CancellationToken ct)
    {
        ModelState.Clear(); TryValidateModel(EmailChange, nameof(EmailChange));
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.RequestEmailCodeAsync(session.Token, EmailChange?.Email?.Trim() ?? "", ct);
        if (result.State == AccountLoadState.Success && result.Value is { } challenge) { ChallengeId = challenge.ChallengeId; Message = "Enviamos o código para o novo e-mail."; } else { ExpireIfNeeded(result.State); Error = result.Message ?? "Não foi possível alterar o e-mail."; }
        await LoadAsync(ct); return Page();
    }
    public async Task<IActionResult> OnPostVerifyEmailCodeAsync(CancellationToken ct)
    {
        ModelState.Clear(); TryValidateModel(Verification, nameof(Verification));
        if (!ModelState.IsValid || Verification.ChallengeId == Guid.Empty) { await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.VerifyEmailCodeAsync(session.Token, Verification.ChallengeId, Verification.Code.Trim(), ct);
        if (result.Value) Message = "E-mail atualizado. As outras sessões foram encerradas."; else { ExpireIfNeeded(result.State); Error = result.Message ?? "Código inválido."; }
        await LoadAsync(ct); return Page();
    }
    public async Task<IActionResult> OnPostLogoutAsync(bool all, CancellationToken ct)
    {
        if (Session is { } session) await client.LogoutAsync(session.Token, all, ct);
        cookies.Clear(); return RedirectToPage("/Account");
    }
    public async Task<IActionResult> OnPostRequestClosureCodeAsync(CancellationToken ct)
    {
        ModelState.Clear();
        if (!ConfirmClosure) { ModelState.AddModelError(nameof(ConfirmClosure), "Marque a confirmação para encerrar a conta."); await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.RequestClosureCodeAsync(session.Token, ct); if (result.State == AccountLoadState.Success && result.Value is { } challenge) { ChallengeId = challenge.ChallengeId; Message = "Enviamos um código para confirmar o encerramento."; } else { ExpireIfNeeded(result.State); Error = result.Message ?? "Não foi possível iniciar o encerramento."; } await LoadAsync(ct); return Page();
    }
    public async Task<IActionResult> OnPostVerifyClosureCodeAsync(CancellationToken ct)
    {
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
        var orders = await client.GetOrdersAsync(session.Token, ct); if (orders.State == AccountLoadState.Success && orders.Value is not null) Orders = orders.Value; else ExpireIfNeeded(orders.State);
        if (!string.IsNullOrWhiteSpace(PublicOrderNumber)) { var order = await client.GetOrderAsync(session.Token, PublicOrderNumber, ct); SelectedOrder = order.Value; }
    }
    private void ExpireIfNeeded(AccountLoadState state) { if (state == AccountLoadState.Unauthorized) cookies.Clear(); }
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
        public CustomerAccountAddress? ToModel() => string.IsNullOrWhiteSpace(Street) ? null : new() { Recipient = Clean(Recipient), Street = Clean(Street), Number = Clean(Number), Complement = string.IsNullOrWhiteSpace(Complement) ? null : Complement.Trim(), Neighborhood = Clean(Neighborhood), City = Clean(City), State = Clean(State).ToUpperInvariant(), PostalCode = Clean(PostalCode), CountryCode = "BR" };
    }
}
