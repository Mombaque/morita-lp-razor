using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class AccountModel(
    ICustomerAccountClient client,
    ICustomerAccountCookieStore cookies,
    IOptions<StorefrontOptions>? storefrontOptions = null) : PageModel
{
    public CustomerAccountProfile? Profile { get; private set; }
    public IReadOnlyList<CustomerAccountAddress> Addresses { get; private set; } = [];
    public StorefrontAccountOrderPage Orders { get; private set; } = new() { Page = 1, PageSize = 20 };
    public PublicOrder? SelectedOrder { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public IReadOnlyList<string> AccountErrors => ModelState.Values.SelectMany(entry => entry.Errors.Take(1)).Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Confira os dados informados." : error.ErrorMessage).Concat(Error is null ? Enumerable.Empty<string>() : [Error]).Distinct(StringComparer.Ordinal).ToArray();
    public string? OrdersError { get; private set; }
    public string? PrivacyPolicyUrl => storefrontOptions?.Value.PrivacyPolicyUrl;
    public bool AccountEnabled => storefrontOptions?.Value.CustomerAccountsEnabled ?? true;
    public bool ChallengeIssued => ChallengeId != Guid.Empty;
    public bool EmailVerificationChallengeIssued => ChallengeKind == "email-verification" && ChallengeIssued;
    public bool PasswordChallengeIssued => ChallengeKind == "password" && ChallengeIssued;
    public bool ProfileIncomplete => Profile is not null && (string.IsNullOrWhiteSpace(Profile.Name) || string.IsNullOrWhiteSpace(Profile.Phone));
    public bool SignedIn => Profile is not null;

    [BindProperty(SupportsGet = true)] public string? Mode { get; set; } = "create";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    [BindProperty(SupportsGet = true)] public int CurrentPage { get; set; } = 1;
    [BindProperty] public RegistrationInput Registration { get; set; } = new();
    [BindProperty] public LoginInput Login { get; set; } = new();
    [BindProperty] public EmailInput EmailForm { get; set; } = new();
    [BindProperty] public PasswordInput PasswordForm { get; set; } = new();
    [BindProperty] public VerificationInput Verification { get; set; } = new();
    [BindProperty] public ProfileInput ProfileForm { get; set; } = new();
    [BindProperty] public AddressInput AddressForm { get; set; } = new();
    [BindProperty] public string AddressLabel { get; set; } = "Meu endereço";
    [BindProperty] public Guid AddressId { get; set; }
    public bool IsEditingAddress => AddressId != Guid.Empty;
    public bool CanAddAddress => Addresses.Count < 10;
    [BindProperty] public Guid ChallengeId { get; set; }
    [BindProperty] public DateTimeOffset? ChallengeExpiresAt { get; set; }
    [BindProperty] public string ChallengeKind { get; set; } = "";
    [BindProperty] public string? PrivacyPolicyVersion { get; set; }
    [BindProperty] public bool ConfirmClosure { get; set; }
    [BindProperty] public string CurrentPassword { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? PublicOrderNumber { get; set; }
    public bool OrdersLoaded { get; private set; }
    private CustomerAccountSession? Session => cookies.Read();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ViewData["Robots"] = "noindex,nofollow";
        Mode = NormalizeMode(Mode); ReturnUrl = SafeReturnUrl(ReturnUrl); await LoadAsync(ct); return Page();
    }

    public async Task<IActionResult> OnGetEditAddressAsync(Guid id, CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ViewData["Robots"] = "noindex,nofollow"; await LoadAsync(ct);
        var address = Addresses.FirstOrDefault(item => item.PublicAddressId == id);
        if (address is null) { Error = "Esse endereço não está disponível."; return Page(); }
        AddressId = address.PublicAddressId; AddressLabel = address.Label; AddressForm = AddressInput.From(address); return Page();
    }

    public async Task<IActionResult> OnPostRegisterAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear(); ReturnUrl = SafeReturnUrl(ReturnUrl); TryValidateModel(Registration, nameof(Registration));
        if (!Registration.AcceptedPrivacyPolicy) ModelState.AddModelError(nameof(Registration.AcceptedPrivacyPolicy), "Aceite a política de privacidade para criar sua conta.");
        if (!ModelState.IsValid) return Page();
        var result = await client.RegisterAsync(Clean(Registration.Email), Clean(Registration.Name), Clean(Registration.Phone), Registration.Password, Registration.AcceptedPrivacyPolicy, storefrontOptions?.Value.PrivacyPolicyVersion ?? "customer-account-v1", ct);
        if (result.State != AccountLoadState.Success || result.Value is null) { Error = result.State == AccountLoadState.Conflict ? "Já existe uma conta com este e-mail." : result.Message ?? "Não foi possível criar sua conta."; return Page(); }
        ChallengeId = result.Value.ChallengeId; ChallengeExpiresAt = result.Value.ExpiresAt; PrivacyPolicyVersion = result.Value.PrivacyPolicyVersion; ChallengeKind = "email-verification"; Mode = "create"; Message = "Enviamos um código para confirmar seu e-mail."; return Page();
    }

    public async Task<IActionResult> OnPostLoginAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear(); ReturnUrl = SafeReturnUrl(ReturnUrl); TryValidateModel(Login, nameof(Login));
        if (!ModelState.IsValid) return Page();
        var result = await client.LoginAsync(Clean(Login.Email), Login.Password, ct);
        if (result.State != AccountLoadState.Success || result.Value.Session.Token.Length == 0) { Error = result.Message ?? "E-mail ou senha inválidos."; return Page(); }
        return EstablishSession(result.Value.Session, result.Value.Profile);
    }

    public async Task<IActionResult> OnPostVerifyEmailAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear(); TryValidateModel(Verification, nameof(Verification));
        if (!EmailVerificationChallengeIssued) ModelState.AddModelError(nameof(Verification.Code), "Solicite um novo código antes de confirmar seu e-mail.");
        if (!ModelState.IsValid) return Page();
        var result = await client.VerifyEmailAsync(ChallengeId, Clean(Verification.Code), true, PrivacyPolicyVersion ?? storefrontOptions?.Value.PrivacyPolicyVersion ?? "customer-account-v1", ct);
        if (result.State != AccountLoadState.Success || result.Value.Session.Token.Length == 0) { Error = result.Message ?? "Código inválido ou expirado."; return Page(); }
        return EstablishSession(result.Value.Session, result.Value.Profile);
    }


    public async Task<IActionResult> OnPostRequestPasswordCodeAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        var hadSession = Session is not null;
        ModelState.Clear(); ReturnUrl = SafeReturnUrl(ReturnUrl); TryValidateModel(EmailForm, nameof(EmailForm));
        if (!ModelState.IsValid) { if (hadSession) await LoadAsync(ct); return Page(); }
        var result = await client.RequestPasswordCodeAsync(Session?.Token, Clean(EmailForm.Email), ct);
        if (result.State != AccountLoadState.Success || result.Value is null) { Error = result.Message ?? "Não foi possível iniciar a redefinição de senha."; if (hadSession) await LoadAsync(ct); return Page(); }
        ChallengeId = result.Value.ChallengeId; ChallengeExpiresAt = result.Value.ExpiresAt; ChallengeKind = "password"; Mode = "forgot"; Message = "Se o e-mail estiver cadastrado, enviaremos um código para redefinir sua senha.";
        if (hadSession) await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear(); TryValidateModel(Verification, nameof(Verification)); TryValidateModel(PasswordForm, nameof(PasswordForm));
        if (!PasswordForm.Password.Equals(PasswordForm.Confirmation, StringComparison.Ordinal)) ModelState.AddModelError(nameof(PasswordForm.Confirmation), "As senhas não coincidem.");
        if (!PasswordChallengeIssued) ModelState.AddModelError(nameof(Verification.Code), "Solicite um novo código antes de alterar sua senha.");
        if (!ModelState.IsValid) { if (Session is not null) await LoadAsync(ct); return Page(); }
        var result = await client.ResetPasswordAsync(Session?.Token, ChallengeId, Clean(Verification.Code), PasswordForm.Password, ct);
        if (result.State != AccountLoadState.Success || result.Value.Session.Token.Length == 0) { Error = result.Message ?? "Código inválido ou expirado."; if (Session is not null) await LoadAsync(ct); return Page(); }
        return EstablishSession(result.Value.Session, result.Value.Profile);
    }

    public async Task<IActionResult> OnPostSaveProfileAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear(); TryValidateModel(ProfileForm, nameof(ProfileForm));
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.UpdateProfileAsync(session.Token, Clean(ProfileForm.Name), Clean(ProfileForm.Phone), ct);
        if (!result.Value) { ExpireIfNeeded(result.State); Error = result.Message ?? "Não foi possível salvar seus dados."; } else Message = "Dados salvos.";
        await LoadAsync(ct); return Page();
    }

    public async Task<IActionResult> OnPostSaveAddressAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound();
        ModelState.Clear(); if (Session is not { } session) return RedirectToPage(); await LoadAsync(ct);
        if (!SignedIn) return cookies.Read() is null ? RedirectToPage() : Page();
        if (AddressId == Guid.Empty && !CanAddAddress) { Error = "Você já atingiu o limite de 10 endereços salvos."; await LoadAsync(ct); return Page(); }
        TryValidateModel(AddressForm, nameof(AddressForm)); if (!ValidateAddress(AddressForm)) { await LoadAsync(ct); return Page(); }
        var value = AddressForm.ToModel(AddressLabel); var result = AddressId == Guid.Empty ? await client.CreateAddressAsync(session.Token, value, ct) : await client.UpdateAddressAsync(session.Token, AddressId, value, ct);
        if (result.State != AccountLoadState.Success) { ExpireIfNeeded(result.State); Error = result.Message ?? "Não foi possível salvar o endereço."; await LoadAsync(ct); return Page(); }
        return Redirect("/conta#enderecos");
    }

    public async Task<IActionResult> OnPostDeleteAddressAsync(Guid id, CancellationToken ct) => await AddressMutationAsync(id, false, ct);
    public async Task<IActionResult> OnPostSetDefaultAddressAsync(Guid id, CancellationToken ct) => await AddressMutationAsync(id, true, ct);
    private async Task<IActionResult> AddressMutationAsync(Guid id, bool setDefault, CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound(); ModelState.Clear(); if (Session is not { } session) return RedirectToPage();
        var result = setDefault ? await client.SetDefaultAddressAsync(session.Token, id, ct) : await client.DeleteAddressAsync(session.Token, id, ct);
        if (result.Value) return Redirect("/conta#enderecos"); ExpireIfNeeded(result.State); Error = result.Message ?? "Não foi possível atualizar os endereços."; await LoadAsync(ct); return Page();
    }

    public async Task<IActionResult> OnPostCloseAsync(CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound(); ModelState.Clear();
        if (!ConfirmClosure) ModelState.AddModelError(nameof(ConfirmClosure), "Marque a confirmação para encerrar a conta.");
        if (string.IsNullOrWhiteSpace(CurrentPassword)) ModelState.AddModelError(nameof(CurrentPassword), "Informe sua senha atual.");
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }
        if (Session is not { } session) return RedirectToPage();
        var result = await client.CloseAsync(session.Token, CurrentPassword, ct);
        if (result.Value) { cookies.Clear(); return RedirectToPage("/Account"); }
        ExpireIfNeeded(result.State); Error = result.Message ?? "Não foi possível encerrar a conta."; await LoadAsync(ct); return Page();
    }

    public async Task<IActionResult> OnPostLogoutAsync(bool all, CancellationToken ct)
    {
        if (!AccountEnabled) return NotFound(); if (Session is { } session) await client.LogoutAsync(session.Token, all, ct); cookies.Clear(); return RedirectToPage("/Account");
    }

    private IActionResult EstablishSession(CustomerAccountSession session, CustomerAccountProfile profile)
    {
        if (!cookies.Write(session.Token, session.ExpiresAt)) { Error = "Não foi possível proteger sua sessão."; return Page(); }
        Profile = profile; return LocalRedirect(SafeReturnUrl(ReturnUrl) ?? "/conta");
    }
    private async Task LoadAsync(CancellationToken ct)
    {
        if (Session is not { } session) return;
        var profile = await client.GetProfileAsync(session.Token, ct);
        if (profile.State == AccountLoadState.Success && profile.Value is { } loadedProfile) { Profile = loadedProfile; ProfileForm = ProfileInput.From(loadedProfile); } else { ExpireIfNeeded(profile.State); Error = profile.Message; return; }
        var addresses = await client.GetAddressesAsync(session.Token, ct);
        if (addresses.State == AccountLoadState.Success && addresses.Value is not null) Addresses = addresses.Value; else if (addresses.State == AccountLoadState.Unauthorized) { cookies.Clear(); Profile = null; Error = addresses.Message ?? "Sua sessão expirou. Entre novamente."; return; } else Error = addresses.Message ?? "Não foi possível carregar seus endereços agora. Tente novamente.";
        var orders = await client.GetOrdersAsync(session.Token, Math.Max(CurrentPage, 1), 20, ct);
        OrdersLoaded = true;
        if (orders.State == AccountLoadState.Success && orders.Value is not null) Orders = orders.Value; else if (orders.State == AccountLoadState.Unauthorized) { cookies.Clear(); Profile = null; Error = orders.Message ?? "Sua sessão expirou. Entre novamente."; return; } else OrdersError = orders.Message ?? "Não foi possível carregar seu histórico agora. Tente novamente em instantes.";
        if (!string.IsNullOrWhiteSpace(PublicOrderNumber)) { var order = await client.GetOrderAsync(session.Token, PublicOrderNumber, ct); if (order.State == AccountLoadState.Unauthorized) { cookies.Clear(); Profile = null; Error = order.Message ?? "Sua sessão expirou. Entre novamente."; return; } SelectedOrder = order.Value; }
    }
    private void ExpireIfNeeded(AccountLoadState state) { if (state == AccountLoadState.Unauthorized) cookies.Clear(); }
    public static string? SafeReturnUrl(string? value) => !string.IsNullOrWhiteSpace(value) && value.StartsWith('/') && !value.StartsWith("//") && !value.Contains('\\', StringComparison.Ordinal) && !value.Contains('\0') && Uri.TryCreate(value, UriKind.Relative, out _) ? value : null;
    private static string NormalizeMode(string? value) => value?.ToLowerInvariant() switch { "signin" or "login" => "signin", "forgot" or "reset" => "forgot", "complete" => "complete", _ => "create" };
    private static string Clean(string? value) => value?.Trim() ?? "";
    private void Required(string? value, string key, string message) { if (string.IsNullOrWhiteSpace(value)) ModelState.AddModelError(key, message); }
    private bool ValidateAddress(AddressInput address)
    {
        Required(AddressLabel, "AddressLabel", "Informe um rótulo para o endereço."); Required(address.Recipient, "AddressForm.Recipient", "Informe o destinatário."); Required(address.Street, "AddressForm.Street", "Informe a rua."); Required(address.Number, "AddressForm.Number", "Informe o número."); Required(address.Neighborhood, "AddressForm.Neighborhood", "Informe o bairro."); Required(address.City, "AddressForm.City", "Informe a cidade.");
        if (!BrazilianStateCodes.Contains(Clean(address.State).ToUpperInvariant())) ModelState.AddModelError("AddressForm.State", "Informe uma UF brasileira válida.");
        if (!ValidPostalCode(address.PostalCode)) ModelState.AddModelError("AddressForm.PostalCode", "Informe um CEP brasileiro válido."); return ModelState.IsValid;
    }
    private static bool ValidPostalCode(string? value) => value is not null && value.Count(char.IsAsciiDigit) == 8 && value.All(character => char.IsAsciiDigit(character) || character is '-' or ' ' or '.');

    public sealed class RegistrationInput
    {
        [Required(ErrorMessage = "Informe seu e-mail.")][EmailAddress(ErrorMessage = "Informe um e-mail válido.")][StringLength(254, ErrorMessage = "O e-mail deve ter no máximo 254 caracteres.")] public string Email { get; set; } = "";
        [Required(ErrorMessage = "Informe seu nome.")][StringLength(120, ErrorMessage = "O nome deve ter no máximo 120 caracteres.")] public string Name { get; set; } = "";
        [Required(ErrorMessage = "Informe seu telefone.")][StringLength(40, ErrorMessage = "O telefone deve ter no máximo 40 caracteres.")] public string Phone { get; set; } = "";
        [Required(ErrorMessage = "Informe uma senha.")][StringLength(72, MinimumLength = 8, ErrorMessage = "A senha deve ter entre 8 e 72 caracteres.")] public string Password { get; set; } = "";
        [Compare(nameof(Password), ErrorMessage = "As senhas não coincidem.")] public string Confirmation { get; set; } = "";
        public bool AcceptedPrivacyPolicy { get; set; }
    }
    public sealed class LoginInput
    {
        [Required(ErrorMessage = "Informe seu e-mail.")][EmailAddress(ErrorMessage = "Informe um e-mail válido.")][StringLength(254, ErrorMessage = "O e-mail deve ter no máximo 254 caracteres.")] public string Email { get; set; } = "";
        [Required(ErrorMessage = "Informe sua senha.")] public string Password { get; set; } = "";
    }
    public sealed class EmailInput
    {
        [Required(ErrorMessage = "Informe seu e-mail.")][EmailAddress(ErrorMessage = "Informe um e-mail válido.")][StringLength(254, ErrorMessage = "O e-mail deve ter no máximo 254 caracteres.")] public string Email { get; set; } = "";
    }
    public sealed class PasswordInput
    {
        [Required(ErrorMessage = "Informe uma senha.")][StringLength(72, MinimumLength = 8, ErrorMessage = "A senha deve ter entre 8 e 72 caracteres.")] public string Password { get; set; } = "";
        [Compare(nameof(Password), ErrorMessage = "As senhas não coincidem.")] public string Confirmation { get; set; } = "";
    }
    public sealed class VerificationInput
    {
        [Required(ErrorMessage = "Informe o código.")][StringLength(6, MinimumLength = 6, ErrorMessage = "O código deve ter exatamente 6 números.")] public string Code { get; set; } = "";
    }
    public sealed class ProfileInput
    {
        [Required(ErrorMessage = "Informe seu nome.")][StringLength(120, ErrorMessage = "O nome deve ter no máximo 120 caracteres.")] public string Name { get; set; } = "";
        [Required(ErrorMessage = "Informe seu telefone.")][StringLength(40, ErrorMessage = "O telefone deve ter no máximo 40 caracteres.")] public string Phone { get; set; } = "";
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
    public sealed record BrazilianStateOption(string Code, string Name);
    public static IReadOnlyList<BrazilianStateOption> BrazilianStates { get; } = [new("AC", "Acre"), new("AL", "Alagoas"), new("AP", "Amapá"), new("AM", "Amazonas"), new("BA", "Bahia"), new("CE", "Ceará"), new("DF", "Distrito Federal"), new("ES", "Espírito Santo"), new("GO", "Goiás"), new("MA", "Maranhão"), new("MT", "Mato Grosso"), new("MS", "Mato Grosso do Sul"), new("MG", "Minas Gerais"), new("PA", "Pará"), new("PB", "Paraíba"), new("PR", "Paraná"), new("PE", "Pernambuco"), new("PI", "Piauí"), new("RJ", "Rio de Janeiro"), new("RN", "Rio Grande do Norte"), new("RS", "Rio Grande do Sul"), new("RO", "Rondônia"), new("RR", "Roraima"), new("SC", "Santa Catarina"), new("SP", "São Paulo"), new("SE", "Sergipe"), new("TO", "Tocantins")];
    private static readonly HashSet<string> BrazilianStateCodes = BrazilianStates.Select(state => state.Code).ToHashSet(StringComparer.Ordinal);
}
