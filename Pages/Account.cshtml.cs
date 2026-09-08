using System.ComponentModel.DataAnnotations;
using System.Linq;
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
    public IReadOnlyList<CustomerAccountAddress> Addresses { get; private set; } = [];
    public StorefrontAccountOrderPage Orders { get; private set; } = new() { Page = 1, PageSize = 20 };
    public PublicOrder? SelectedOrder { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<string> AccountErrors => ModelState.Values
        .SelectMany(entry => entry.Errors.Take(1))
        .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Confira os dados informados." : error.ErrorMessage)
        .Concat(Error is null ? Enumerable.Empty<string>() : [Error])
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    public string? OrdersError { get; private set; }
    public bool OrdersLoaded { get; private set; }
    public string? PrivacyPolicyUrl => storefrontOptions?.Value.PrivacyPolicyUrl;
    public bool AccountEnabled => storefrontOptions?.Value.CustomerAccountsEnabled ?? true;
    public bool ChallengeIssued => ChallengeId != Guid.Empty;
    public bool EmailChallengeIssued => ChallengeKind == "email" && ChallengeId != Guid.Empty;
    public bool ClosureChallengeIssued => ChallengeKind == "closure" && ChallengeId != Guid.Empty;
    public bool SignedIn => Profile is not null;
    [BindProperty(SupportsGet = true)] public string? Mode { get; set; } = "create";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    [BindProperty(SupportsGet = true)] public int CurrentPage { get; set; } = 1;
    [BindProperty] public EmailInput EmailForm { get; set; } = new();
    [BindProperty] public VerificationInput Verification { get; set; } = new();
    [BindProperty] public ProfileInput ProfileForm { get; set; } = new();
    [BindProperty] public AddressInput AddressForm { get; set; } = new();
    [BindProperty] public string AddressLabel { get; set; } = "Meu endereço";
    [BindProperty] public Guid AddressId { get; set; }
    public bool IsEditingAddress => AddressId != Guid.Empty;
    public bool CanAddAddress => Addresses.Count < 10;
    [BindProperty] public EmailChangeInput EmailChange { get; set; } = new();
    [BindProperty] public Guid ChallengeId { get; set; }
    [BindProperty] public DateTimeOffset? ChallengeExpiresAt { get; set; }
    [BindProperty] public string ChallengeKind { get; set; } = "";
    [BindProperty] public string? ChallengeTargetEmail { get; set; }
    [BindProperty] public string? PrivacyPolicyVersion { get; set; }
    [BindProperty] public bool AcceptedPrivacyPolicy { get; set; }
    [BindProperty] public bool ConfirmClosure { get; set; }
    [BindProperty(SupportsGet = true)] public string? PublicOrderNumber { get; set; }
    private CustomerAccountSession? Session => cookies.Read();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ViewData["Robots"] = "noindex,nofollow";
        Mode = string.Equals(Mode, "create", StringComparison.OrdinalIgnoreCase) ? "create" : "signin";
        ReturnUrl = SafeReturnUrl(ReturnUrl);
        await LoadAsync(ct);
        return Page();
    }
    public async Task<IActionResult> OnGetEditAddressAsync(Guid id, CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ViewData["Robots"] = "noindex,nofollow";
        await LoadAsync(ct);
        var address = Addresses.FirstOrDefault(item => item.PublicAddressId == id);
        if (address is null)
        {
            Error = "Esse endereço não está disponível.";
            return Page();
        }

        AddressId = address.PublicAddressId;
        AddressLabel = address.Label;
        AddressForm = AddressInput.From(address);
        return Page();
    }
    public async Task<IActionResult> OnPostRequestCodeAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear();
        ChallengeKind = "signin";
        ChallengeId = Guid.Empty;
        ChallengeExpiresAt = null;
        TryValidateModel(EmailForm, nameof(EmailForm));
        if (!ModelState.IsValid) return Page();
        ReturnUrl = SafeReturnUrl(ReturnUrl);
        var result = await client.RequestCodeAsync(EmailForm?.Email?.Trim() ?? "", ct);
        if (result.State != AccountLoadState.Success || result.Value is null) { Error = result.Message ?? "Não foi possível enviar o código."; return Page(); }
        ChallengeId = result.Value.ChallengeId; ChallengeExpiresAt = result.Value.ExpiresAt; PrivacyPolicyVersion = result.Value.PrivacyPolicyVersion; ChallengeKind = "signin";
        Message = "Enviamos um código para seu e-mail. Ele expira em poucos minutos."; return Page();
    }
    public async Task<IActionResult> OnPostVerifyCodeAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear(); TryValidateModel(Verification, nameof(Verification));
        if (ChallengeId == Guid.Empty || !ChallengeIssued || ChallengeKind != "signin") ModelState.AddModelError(nameof(Verification.Code), "Solicite um novo código antes de confirmar a entrada.");
        if (string.IsNullOrWhiteSpace(PrivacyPolicyVersion)) ModelState.AddModelError(nameof(PrivacyPolicyVersion), "Não foi possível confirmar a versão da política de privacidade. Solicite um novo código.");
        if (!AcceptedPrivacyPolicy) ModelState.AddModelError(nameof(AcceptedPrivacyPolicy), "Aceite a política de privacidade para continuar. Isso é necessário para criar uma conta nova.");
        if (!ModelState.IsValid) return Page();
        var result = await client.VerifyCodeAsync(ChallengeId, Verification?.Code?.Trim() ?? "", AcceptedPrivacyPolicy, PrivacyPolicyVersion!, ct);
        if (result.State != AccountLoadState.Success || result.Value.Session is null || result.Value.Session.Token.Length == 0) { Error = result.Message ?? "Código inválido ou expirado."; return Page(); }
        if (!cookies.Write(result.Value.Session.Token, result.Value.Session.ExpiresAt)) { Error = "Não foi possível proteger sua sessão."; return Page(); }
        return LocalRedirect(SafeReturnUrl(ReturnUrl) ?? "/conta");
    }
    public async Task<IActionResult> OnPostSaveProfileAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear(); TryValidateModel(ProfileForm, nameof(ProfileForm));
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.UpdateProfileAsync(session.Token, Clean(ProfileForm?.Name), Clean(ProfileForm?.Phone), ct);
        if (!result.Value) { ExpireIfNeeded(result.State); Error = result.Message ?? "Não foi possível salvar seus dados."; }
        else Message = "Dados salvos.";
        await LoadAsync(ct); return Page();
    }
    public async Task<IActionResult> OnPostSaveAddressAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear();
        if (Session is not { } session) return RedirectToPage();
        await LoadAsync(ct);
        if (!SignedIn) return cookies.Read() is null ? RedirectToPage() : Page();
        if (AddressId == Guid.Empty && !CanAddAddress)
        {
            Error = "Você já atingiu o limite de 10 endereços salvos.";
            await LoadAsync(ct);
            return Page();
        }
        TryValidateModel(AddressForm, nameof(AddressForm));
        if (!ValidateAddress(AddressForm))
        {
            await LoadAsync(ct);
            return Page();
        }
        var value = AddressForm.ToModel(AddressLabel);
        var result = AddressId == Guid.Empty ? await client.CreateAddressAsync(session.Token, value, ct) : await client.UpdateAddressAsync(session.Token, AddressId, value, ct);
        if (result.State != AccountLoadState.Success) { ExpireIfNeeded(result.State); Error = result.Message ?? "Não foi possível salvar o endereço."; await LoadAsync(ct); return Page(); }
        return Redirect("/conta#enderecos");
    }
    public async Task<IActionResult> OnPostDeleteAddressAsync(Guid id, CancellationToken ct) => await AddressMutationAsync(id, false, ct);
    public async Task<IActionResult> OnPostSetDefaultAddressAsync(Guid id, CancellationToken ct) => await AddressMutationAsync(id, true, ct);
    private async Task<IActionResult> AddressMutationAsync(Guid id, bool setDefault, CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear();
        if (Session is not { } session) return RedirectToPage();
        var result = setDefault
            ? await client.SetDefaultAddressAsync(session.Token, id, ct)
            : await client.DeleteAddressAsync(session.Token, id, ct);
        if (result.Value) return Redirect("/conta#enderecos");
        ExpireIfNeeded(result.State);
        Error = result.Message ?? "Não foi possível atualizar os endereços.";
        await LoadAsync(ct); return Page();
    }
    public async Task<IActionResult> OnPostRequestEmailCodeAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear();
        ChallengeKind = "email";
        ChallengeId = Guid.Empty;
        ChallengeExpiresAt = null;
        ChallengeTargetEmail = null;
        TryValidateModel(EmailChange, nameof(EmailChange));
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.RequestEmailCodeAsync(session.Token, EmailChange?.Email?.Trim() ?? "", ct);
        if (result.State == AccountLoadState.Success && result.Value is { } challenge) { ChallengeId = challenge.ChallengeId; ChallengeExpiresAt = challenge.ExpiresAt; ChallengeTargetEmail = EmailChange?.Email?.Trim(); ChallengeKind = "email"; Message = "Enviamos o código para o novo e-mail."; } else { ExpireIfNeeded(result.State); Error = result.Message ?? "Não foi possível alterar o e-mail."; }
        await LoadAsync(ct); return Page();
    }
    public async Task<IActionResult> OnPostVerifyEmailCodeAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear(); TryValidateModel(Verification, nameof(Verification));
        if (!ModelState.IsValid || ChallengeId == Guid.Empty || !EmailChallengeIssued) { await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.VerifyEmailCodeAsync(session.Token, ChallengeId, Verification.Code?.Trim() ?? "", ct);
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
        ChallengeKind = "closure";
        ChallengeId = Guid.Empty;
        ChallengeExpiresAt = null;
        if (!ConfirmClosure) { ModelState.AddModelError(nameof(ConfirmClosure), "Marque a confirmação para encerrar a conta."); await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.RequestClosureCodeAsync(session.Token, ct); if (result.State == AccountLoadState.Success && result.Value is { } challenge) { ChallengeId = challenge.ChallengeId; ChallengeExpiresAt = challenge.ExpiresAt; ChallengeKind = "closure"; Message = "Enviamos um código para confirmar o encerramento."; } else { ExpireIfNeeded(result.State); Error = result.Message ?? "Não foi possível iniciar o encerramento."; } await LoadAsync(ct); return Page();
    }
    public async Task<IActionResult> OnPostVerifyClosureCodeAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear();
        TryValidateModel(Verification, nameof(Verification));
        if (!ConfirmClosure) ModelState.AddModelError(nameof(ConfirmClosure), "Marque a confirmação para encerrar a conta.");
        if (!ModelState.IsValid || ChallengeId == Guid.Empty || !ClosureChallengeIssued) { await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.VerifyClosureCodeAsync(session.Token, ChallengeId, Verification.Code.Trim(), ct); if (result.Value) { cookies.Clear(); return RedirectToPage("/Account"); } ExpireIfNeeded(result.State); Error = result.Message ?? "Código inválido."; await LoadAsync(ct); return Page();
    }
    private async Task LoadAsync(CancellationToken ct)
    {
        if (Session is not { } session) return;
        var profile = await client.GetProfileAsync(session.Token, ct);
        if (profile.State == AccountLoadState.Success && profile.Value is { } loadedProfile) { Profile = loadedProfile; ProfileForm = ProfileInput.From(loadedProfile); }
        else { ExpireIfNeeded(profile.State); Error = profile.Message; return; }
        var addresses = await client.GetAddressesAsync(session.Token, ct);
        if (addresses.State == AccountLoadState.Success && addresses.Value is not null) Addresses = addresses.Value;
        else if (addresses.State == AccountLoadState.Unauthorized) { cookies.Clear(); Profile = null; Error = addresses.Message ?? "Sua sessão expirou. Entre novamente."; return; }
        else Error = addresses.Message ?? "Não foi possível carregar seus endereços agora. Tente novamente.";
        var orders = await client.GetOrdersAsync(session.Token, Math.Max(CurrentPage, 1), 20, ct); OrdersLoaded = true;
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
    public static string? SafeReturnUrl(string? value) => !string.IsNullOrWhiteSpace(value) && value.StartsWith('/') && !value.StartsWith("//") && !value.Contains("\\", StringComparison.Ordinal) && !value.Contains('\0') && Uri.TryCreate(value, UriKind.Relative, out _) ? value : null;
    private void Required(string? value, string key, string message) { if (string.IsNullOrWhiteSpace(value)) ModelState.AddModelError(key, message); }
    private bool ValidateAddress(AddressInput address)
    {
        Required(AddressLabel, "AddressLabel", "Informe um rótulo para o endereço."); Required(address.Recipient, "AddressForm.Recipient", "Informe o destinatário."); Required(address.Street, "AddressForm.Street", "Informe a rua."); Required(address.Number, "AddressForm.Number", "Informe o número."); Required(address.Neighborhood, "AddressForm.Neighborhood", "Informe o bairro."); Required(address.City, "AddressForm.City", "Informe a cidade.");
        if (!BrazilianStateCodes.Contains(Clean(address.State).ToUpperInvariant())) ModelState.AddModelError("AddressForm.State", "Informe uma UF brasileira válida.");
        if (!ValidPostalCode(address.PostalCode)) ModelState.AddModelError("AddressForm.PostalCode", "Informe um CEP brasileiro válido.");
        return ModelState.IsValid;
    }
    private static bool ValidPostalCode(string? value) => value is not null && value.Count(char.IsAsciiDigit) == 8 && value.All(character => char.IsAsciiDigit(character) || character is '-' or ' ' or '.');
    public sealed record BrazilianStateOption(string Code, string Name);
    public static IReadOnlyList<BrazilianStateOption> BrazilianStates { get; } =
    [
        new("AC", "Acre"),
        new("AL", "Alagoas"),
        new("AP", "Amapá"),
        new("AM", "Amazonas"),
        new("BA", "Bahia"),
        new("CE", "Ceará"),
        new("DF", "Distrito Federal"),
        new("ES", "Espírito Santo"),
        new("GO", "Goiás"),
        new("MA", "Maranhão"),
        new("MT", "Mato Grosso"),
        new("MS", "Mato Grosso do Sul"),
        new("MG", "Minas Gerais"),
        new("PA", "Pará"),
        new("PB", "Paraíba"),
        new("PR", "Paraná"),
        new("PE", "Pernambuco"),
        new("PI", "Piauí"),
        new("RJ", "Rio de Janeiro"),
        new("RN", "Rio Grande do Norte"),
        new("RS", "Rio Grande do Sul"),
        new("RO", "Rondônia"),
        new("RR", "Roraima"),
        new("SC", "Santa Catarina"),
        new("SP", "São Paulo"),
        new("SE", "Sergipe"),
        new("TO", "Tocantins")
    ];
    private static readonly HashSet<string> BrazilianStateCodes = BrazilianStates.Select(state => state.Code).ToHashSet(StringComparer.Ordinal);
    private static string Clean(string? value) => value?.Trim() ?? "";
    public sealed class EmailInput
    {
        [Required(ErrorMessage = "Informe seu e-mail.")]
        [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
        [StringLength(254, ErrorMessage = "O e-mail deve ter no máximo 254 caracteres.")]
        public string Email { get; set; } = "";
    }

    public sealed class VerificationInput
    {
        [Required(ErrorMessage = "Informe o código.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "O código deve ter exatamente 6 números.")]
        public string Code { get; set; } = "";
    }

    public sealed class EmailChangeInput
    {
        [Required(ErrorMessage = "Informe o novo e-mail.")]
        [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
        [StringLength(254, ErrorMessage = "O e-mail deve ter no máximo 254 caracteres.")]
        public string Email { get; set; } = "";
    }
    public sealed class ProfileInput
    {
        [Required(ErrorMessage = "Informe seu nome.")]
        [StringLength(120, ErrorMessage = "O nome deve ter no máximo 120 caracteres.")]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Informe seu telefone.")]
        [StringLength(40, ErrorMessage = "O telefone deve ter no máximo 40 caracteres.")]
        public string Phone { get; set; } = "";
        public static ProfileInput From(CustomerAccountProfile p) => new() { Name = p.Name ?? "", Phone = p.Phone ?? "" };
    }
    public sealed class AddressInput
    {
        [StringLength(120, ErrorMessage = "O destinatário deve ter no máximo 120 caracteres.")] public string? Recipient { get; set; } = "";
        [StringLength(160, ErrorMessage = "A rua deve ter no máximo 160 caracteres.")] public string? Street { get; set; } = "";
        [StringLength(40, ErrorMessage = "O número deve ter no máximo 40 caracteres.")] public string? Number { get; set; } = "";
        [StringLength(160, ErrorMessage = "O complemento deve ter no máximo 160 caracteres.")] public string? Complement { get; set; } = "";
        [StringLength(120, ErrorMessage = "O bairro deve ter no máximo 120 caracteres.")] public string? Neighborhood { get; set; } = "";
        [StringLength(120, ErrorMessage = "A cidade deve ter no máximo 120 caracteres.")] public string? City { get; set; } = "";
        [StringLength(2, ErrorMessage = "A UF deve ter 2 letras.")] public string? State { get; set; } = "";
        [StringLength(10, ErrorMessage = "O CEP deve ter no máximo 10 caracteres.")] public string? PostalCode { get; set; } = "";
        public bool HasAnyValue => new[] { Recipient, Street, Number, Complement, Neighborhood, City, State, PostalCode }.Any(value => !string.IsNullOrWhiteSpace(value));
        public CustomerAccountAddress ToModel(string label) => new() { Label = Clean(label), Recipient = Clean(Recipient), Street = Clean(Street), Number = Clean(Number), Complement = string.IsNullOrWhiteSpace(Complement) ? null : Complement.Trim(), Neighborhood = Clean(Neighborhood), City = Clean(City), State = Clean(State).ToUpperInvariant(), PostalCode = Clean(PostalCode), CountryCode = "BR" };
        public static AddressInput From(CustomerAccountAddress address) => new() { Recipient = address.Recipient, Street = address.Street, Number = address.Number, Complement = address.Complement ?? "", Neighborhood = address.Neighborhood, City = address.City, State = address.State, PostalCode = address.PostalCode };
    }
}
