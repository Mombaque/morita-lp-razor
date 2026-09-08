using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public interface ICustomerAccountClient
{
    Task<AccountResult<AccountCodeChallenge>> RequestCodeAsync(string email, CancellationToken cancellationToken = default);
    Task<AccountResult<(CustomerAccountSession Session, CustomerAccountProfile Profile)>> VerifyCodeAsync(Guid challengeId, string code, bool acceptedPrivacyPolicy, string privacyPolicyVersion, CancellationToken cancellationToken = default);
    Task<AccountResult<CustomerAccountProfile>> GetProfileAsync(string token, CancellationToken cancellationToken = default);
    Task<AccountResult<bool>> UpdateProfileAsync(string token, string? name, string? phone, CancellationToken cancellationToken = default);
    Task<AccountResult<IReadOnlyList<CustomerAccountAddress>>> GetAddressesAsync(string token, CancellationToken cancellationToken = default);
    Task<AccountResult<CustomerAccountAddress>> CreateAddressAsync(string token, CustomerAccountAddress address, CancellationToken cancellationToken = default);
    Task<AccountResult<CustomerAccountAddress>> UpdateAddressAsync(string token, Guid id, CustomerAccountAddress address, CancellationToken cancellationToken = default);
    Task<AccountResult<bool>> DeleteAddressAsync(string token, Guid id, CancellationToken cancellationToken = default);
    Task<AccountResult<bool>> SetDefaultAddressAsync(string token, Guid id, CancellationToken cancellationToken = default);
    Task<AccountResult<AccountCodeChallenge>> RequestEmailCodeAsync(string token, string email, CancellationToken cancellationToken = default);
    Task<AccountResult<bool>> VerifyEmailCodeAsync(string token, Guid challengeId, string code, CancellationToken cancellationToken = default);
    Task<AccountResult<AccountCodeChallenge>> RequestClosureCodeAsync(string token, CancellationToken cancellationToken = default);
    Task<AccountResult<bool>> VerifyClosureCodeAsync(string token, Guid challengeId, string code, CancellationToken cancellationToken = default);
    Task<AccountResult<bool>> LogoutAsync(string token, bool all, CancellationToken cancellationToken = default);
    Task<AccountResult<StorefrontAccountOrderPage>> GetOrdersAsync(string token, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<AccountResult<PublicOrder>> GetOrderAsync(string token, string number, CancellationToken cancellationToken = default);
    Task<AccountResult<bool>> ClaimOrderAsync(string token, string number, string accessToken, CancellationToken cancellationToken = default);
}

public sealed class CustomerAccountClient(
    HttpClient httpClient,
    IOptions<CatalogApiOptions> options,
    ILogger<CustomerAccountClient> logger,
    ICustomerAccountCookieStore cookies,
    TimeProvider timeProvider,
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment environment) : ICustomerAccountClient
{
    private readonly CatalogApiOptions settings = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public Task<AccountResult<AccountCodeChallenge>> RequestCodeAsync(string email, CancellationToken ct = default) => SendAsync<AccountCodeChallenge>(HttpMethod.Post, "v1/storefront/account/auth/request-code", new { email }, null, ct);
    public async Task<AccountResult<(CustomerAccountSession Session, CustomerAccountProfile Profile)>> VerifyCodeAsync(Guid challengeId, string code, bool accepted, string version, CancellationToken ct = default)
    {
        var result = await SendAsync<SessionDto>(HttpMethod.Post, "v1/storefront/account/auth/verify-code", new { challengeId, code, acceptedPrivacyPolicy = accepted, privacyPolicyVersion = version }, null, ct);
        if (result.State != AccountLoadState.Success || result.Value is null)
            return AccountResult<(CustomerAccountSession, CustomerAccountProfile)>.Failure(result.State, result.Message);

        return new(result.State, (new(result.Value.SessionToken, timeProvider.GetUtcNow(), result.Value.ExpiresAt), Map(result.Value.Profile)), result.Message);
    }
    public async Task<AccountResult<CustomerAccountProfile>> GetProfileAsync(string token, CancellationToken ct = default)
    {
        var result = await SendAsync<ProfileDto>(HttpMethod.Get, "v1/storefront/account", null, token, ct);
        return result.State == AccountLoadState.Success && result.Value is not null ? new(result.State, Map(result.Value), result.Message) : AccountResult<CustomerAccountProfile>.Failure(result.State, result.Message);
    }
    public Task<AccountResult<bool>> UpdateProfileAsync(string token, string? name, string? phone, CancellationToken ct = default) => NoContentAsync(HttpMethod.Put, "v1/storefront/account", new { name, phone }, token, ct);
    public async Task<AccountResult<IReadOnlyList<CustomerAccountAddress>>> GetAddressesAsync(string token, CancellationToken ct = default)
    {
        var result = await SendAsync<List<AddressDto>>(HttpMethod.Get, "v1/storefront/account/addresses", null, token, ct);
        return result.State == AccountLoadState.Success && result.Value is not null ? new(result.State, result.Value.Select(Map).ToList(), result.Message) : AccountResult<IReadOnlyList<CustomerAccountAddress>>.Failure(result.State, result.Message);
    }
    public async Task<AccountResult<CustomerAccountAddress>> CreateAddressAsync(string token, CustomerAccountAddress address, CancellationToken ct = default) => await AddressAsync(HttpMethod.Post, "v1/storefront/account/addresses", address, token, ct);
    public async Task<AccountResult<CustomerAccountAddress>> UpdateAddressAsync(string token, Guid id, CustomerAccountAddress address, CancellationToken ct = default) => await AddressAsync(HttpMethod.Put, $"v1/storefront/account/addresses/{id:D}", address, token, ct);
    public Task<AccountResult<bool>> DeleteAddressAsync(string token, Guid id, CancellationToken ct = default) => NoContentAsync(HttpMethod.Delete, $"v1/storefront/account/addresses/{id:D}", null, token, ct);
    public Task<AccountResult<bool>> SetDefaultAddressAsync(string token, Guid id, CancellationToken ct = default) => NoContentAsync(HttpMethod.Post, $"v1/storefront/account/addresses/{id:D}/default", null, token, ct);
    public Task<AccountResult<AccountCodeChallenge>> RequestEmailCodeAsync(string token, string email, CancellationToken ct = default) => SendAsync<AccountCodeChallenge>(HttpMethod.Post, "v1/storefront/account/email/request-code", new { email }, token, ct);
    public Task<AccountResult<bool>> VerifyEmailCodeAsync(string token, Guid id, string code, CancellationToken ct = default) => NoContentAsync(HttpMethod.Post, "v1/storefront/account/email/verify-code", new { challengeId = id, code }, token, ct);
    public Task<AccountResult<AccountCodeChallenge>> RequestClosureCodeAsync(string token, CancellationToken ct = default) => SendAsync<AccountCodeChallenge>(HttpMethod.Post, "v1/storefront/account/close/request-code", null, token, ct);
    public Task<AccountResult<bool>> VerifyClosureCodeAsync(string token, Guid id, string code, CancellationToken ct = default) => NoContentAsync(HttpMethod.Post, "v1/storefront/account/close/verify-code", new { challengeId = id, code }, token, ct);
    public Task<AccountResult<bool>> LogoutAsync(string token, bool all, CancellationToken ct = default) => NoContentAsync(HttpMethod.Post, $"v1/storefront/account/{(all ? "logout-all" : "logout")}", null, token, ct);
    public Task<AccountResult<StorefrontAccountOrderPage>> GetOrdersAsync(string token, int page = 1, int pageSize = 20, CancellationToken ct = default) => SendAsync<StorefrontAccountOrderPage>(HttpMethod.Get, $"v1/storefront/account/orders?page={Math.Max(page, 1)}&pageSize={Math.Clamp(pageSize, 1, 20)}", null, token, ct);
    public Task<AccountResult<PublicOrder>> GetOrderAsync(string token, string number, CancellationToken ct = default) => SendAsync<PublicOrder>(HttpMethod.Get, $"v1/storefront/account/orders/{Uri.EscapeDataString(number)}", null, token, ct);
    public Task<AccountResult<bool>> ClaimOrderAsync(string token, string number, string accessToken, CancellationToken ct = default) => NoContentAsync(HttpMethod.Post, $"v1/storefront/account/orders/{Uri.EscapeDataString(number)}/claim", null, token, ct, ("X-Order-Access-Token", accessToken));
    private async Task<AccountResult<bool>> NoContentAsync(HttpMethod method, string path, object? body, string token, CancellationToken ct, (string Name, string Value)? extra = null)
    {
        var result = await SendAsync<JsonElement>(method, path, body, token, ct, extra);
        return new(result.State, result.State == AccountLoadState.Success, result.Message);
    }
    private async Task<AccountResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, string? token, CancellationToken ct, (string Name, string Value)? extra = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 30)));
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
            if (!string.IsNullOrWhiteSpace(token)) request.Headers.TryAddWithoutValidation("X-Storefront-Session", token);
            if (extra is not null) request.Headers.TryAddWithoutValidation(extra.Value.Name, extra.Value.Value);
            if (httpContextAccessor.HttpContext is { } context)
                request.Headers.TryAddWithoutValidation("X-Morita-Client-IP", ClientIdentityResolver.Resolve(context, environment));
            if (!string.IsNullOrWhiteSpace(settings.ProxySecret))
                request.Headers.TryAddWithoutValidation("X-Morita-Proxy-Secret", settings.ProxySecret);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                cookies.Clear();
                return AccountResult<T>.Failure(AccountLoadState.Unauthorized, "Sua sessão expirou. Entre novamente.");
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return AccountResult<T>.Failure(AccountLoadState.NotFound);
            if (response.StatusCode == HttpStatusCode.Conflict) return AccountResult<T>.Failure(AccountLoadState.Conflict, "Este pedido já pertence a outra conta.");
            if (response.StatusCode == (HttpStatusCode)429) return AccountResult<T>.Failure(AccountLoadState.RateLimited, "Muitas tentativas. Aguarde um pouco.");
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity) return AccountResult<T>.Failure(AccountLoadState.Validation, "Revise os dados informados.");
            if (response.StatusCode == HttpStatusCode.NoContent) return new(AccountLoadState.Success, default);
            if ((int)response.StatusCode >= 500) return AccountResult<T>.Failure(AccountLoadState.Unavailable, "O serviço está temporariamente indisponível.");
            if (!response.IsSuccessStatusCode) return AccountResult<T>.Failure(AccountLoadState.Unavailable);
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, timeout.Token);
            return value is null ? AccountResult<T>.Failure(AccountLoadState.Malformed) : new(AccountLoadState.Success, value);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { logger.LogWarning("Customer account API request timed out"); return AccountResult<T>.Failure(AccountLoadState.Timeout, "A confirmação demorou. Tente novamente."); }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "Customer account API unavailable"); return AccountResult<T>.Failure(AccountLoadState.Unavailable, "Não foi possível acessar o serviço agora."); }
        catch (JsonException ex) { logger.LogWarning(ex, "Customer account API response malformed"); return AccountResult<T>.Failure(AccountLoadState.Malformed); }
    }
    private async Task<AccountResult<CustomerAccountAddress>> AddressAsync(HttpMethod method, string path, CustomerAccountAddress address, string token, CancellationToken ct)
    {
        var result = await SendAsync<AddressDto>(method, path, new { address.Label, address.Recipient, address.Street, address.Number, address.Complement, address.Neighborhood, address.City, address.State, address.PostalCode, address.CountryCode }, token, ct);
        return result.State == AccountLoadState.Success && result.Value is not null ? new(result.State, Map(result.Value), result.Message) : AccountResult<CustomerAccountAddress>.Failure(result.State, result.Message);
    }
    private static CustomerAccountProfile Map(ProfileDto? x) => x is null ? new() : new() { AccountId = x.AccountId, Email = x.Email ?? "", Name = x.Name, Phone = x.Phone };
    private static CustomerAccountAddress Map(AddressDto x) => new() { PublicAddressId = x.PublicAddressId, Label = x.Label, IsDefault = x.IsDefault, Recipient = x.Recipient, Street = x.Street, Number = x.Number, Complement = x.Complement, Neighborhood = x.Neighborhood, City = x.City, State = x.State, PostalCode = x.PostalCode, CountryCode = x.CountryCode };
    private sealed class SessionDto { public string SessionToken { get; set; } = ""; public DateTimeOffset ExpiresAt { get; set; } public ProfileDto Profile { get; set; } = new(); }
    private sealed class ProfileDto { public Guid AccountId { get; set; } public string? Email { get; set; } public string? Name { get; set; } public string? Phone { get; set; } }
    private sealed class AddressDto { public Guid PublicAddressId { get; set; } public string Label { get; set; } = ""; public bool IsDefault { get; set; } public string Recipient { get; set; } = ""; public string Street { get; set; } = ""; public string Number { get; set; } = ""; public string? Complement { get; set; } public string Neighborhood { get; set; } = ""; public string City { get; set; } = ""; public string State { get; set; } = ""; public string PostalCode { get; set; } = ""; public string CountryCode { get; set; } = "BR"; }
}
