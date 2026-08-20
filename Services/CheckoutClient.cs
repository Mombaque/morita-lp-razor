using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public sealed class CheckoutClient(
    HttpClient httpClient,
    IOptions<CatalogApiOptions> options,
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment environment,
    ILogger<CheckoutClient> logger) : ICheckoutClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CatalogApiOptions options = options.Value;

    public async Task<CheckoutConfigurationResult> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<ConfigurationDto>(HttpMethod.Get, "v1/storefront/checkout/configuration", null, null, cancellationToken);
        if (result.State != CheckoutLoadState.Success || result.Value is null || !ValidConfiguration(result.Value)) return CheckoutConfigurationResult.Failure(result.State == CheckoutLoadState.Success ? CheckoutLoadState.Malformed : result.State);
        return new(CheckoutLoadState.Success, new() { PickupEnabled = result.Value.PickupEnabled, PublicPickupId = result.Value.PublicPickupId, Currency = result.Value.Currency!, Pickup = result.Value.Pickup is null ? null : Map(result.Value.Pickup) });
    }

    public async Task<CheckoutResult> CreateAsync(CheckoutCreateRequest request, string idempotencyKey, string accessToken, CancellationToken cancellationToken = default)
    {
        var body = new CreateDto { Lines = request.Lines.Select(x => new LineRequestDto { PublicOfferId = x.PublicOfferId, Quantity = x.Quantity }).ToList(), Contact = new() { Name = request.Contact.Name, Email = request.Contact.Email, Phone = request.Contact.Phone }, Fulfillment = new() { Method = "pickup", PublicPickupId = request.PublicPickupId } };
        var result = await SendAsync<ResponseDto>(HttpMethod.Post, "v1/storefront/checkout", body, ("Idempotency-Key", idempotencyKey), cancellationToken, accessToken);
        return MapResult(result, request.Lines, request.PublicPickupId);
    }

    public async Task<CheckoutResult> GetAsync(Guid publicCheckoutId, string accessToken, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<ResponseDto>(HttpMethod.Get, $"v1/storefront/checkouts/{publicCheckoutId:D}", null, null, cancellationToken, accessToken);
        return MapResult(result);
    }

    public async Task<CheckoutResult> CancelAsync(Guid publicCheckoutId, string accessToken, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<ResponseDto>(HttpMethod.Post, $"v1/storefront/checkouts/{publicCheckoutId:D}/cancel", null, null, cancellationToken, accessToken);
        return result.Status == HttpStatusCode.NoContent ? new(CheckoutLoadState.Success, null) : MapResult(result);
    }

    private CheckoutResult MapResult(
        ReadResult<ResponseDto> result,
        IReadOnlyList<CartLine>? expectedLines = null,
        Guid? expectedPickupId = null)
    {
        if (result.State != CheckoutLoadState.Success) return CheckoutResult.Failure(result.State, result.Message);
        return result.Value is not null && TryMap(result.Value, expectedLines, expectedPickupId, out var checkout) ? new(CheckoutLoadState.Success, checkout) : CheckoutResult.Failure(CheckoutLoadState.Malformed);
    }

    private async Task<ReadResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, (string Name, string Value)? header, CancellationToken callerToken, string? accessToken = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 30)));
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
            if (header is not null) request.Headers.TryAddWithoutValidation(header.Value.Name, header.Value.Value);
            if (!string.IsNullOrWhiteSpace(accessToken)) request.Headers.TryAddWithoutValidation("X-Checkout-Access-Token", accessToken);
            if (httpContextAccessor.HttpContext is { } context)
            {
                request.Headers.TryAddWithoutValidation("X-Morita-Client-IP", ClientIdentityResolver.Resolve(context, environment));
            }
            if (!string.IsNullOrWhiteSpace(options.ProxySecret))
            {
                request.Headers.TryAddWithoutValidation("X-Morita-Proxy-Secret", options.ProxySecret);
            }
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NoContent) return new(response.StatusCode, CheckoutLoadState.Success, default, null);
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity) return new(response.StatusCode, CheckoutLoadState.Validation, default, "Não foi possível reservar os itens com os dados atuais.");
            if (response.StatusCode == HttpStatusCode.Conflict) return new(response.StatusCode, CheckoutLoadState.Conflict, default, "A tentativa de checkout mudou. Tente novamente.");
            if (response.StatusCode == HttpStatusCode.NotFound) return new(response.StatusCode, CheckoutLoadState.NotFound, default, "Esta reserva não está disponível.");
            if (response.StatusCode == (HttpStatusCode)429) return new(response.StatusCode, CheckoutLoadState.RateLimited, default, "Muitas tentativas. Aguarde um pouco.");
            if ((int)response.StatusCode >= 500) return new(response.StatusCode, CheckoutLoadState.Unavailable, default, "O serviço está temporariamente indisponível.");
            if (!response.IsSuccessStatusCode) return new(response.StatusCode, CheckoutLoadState.Unavailable, default, null);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            return new(response.StatusCode, CheckoutLoadState.Success, await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, timeout.Token), null);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested) { logger.LogWarning("Checkout API request timed out"); return new(null, CheckoutLoadState.Timeout, default, "A confirmação demorou. Seus dados foram preservados; tente novamente."); }
        catch (HttpRequestException exception) { logger.LogWarning(exception, "Checkout API unavailable"); return new(null, CheckoutLoadState.Unavailable, default, "Não foi possível acessar o serviço. Tente novamente."); }
        catch (JsonException exception) { logger.LogWarning(exception, "Checkout API response malformed"); return new(null, CheckoutLoadState.Malformed, default, "A resposta do serviço não pôde ser validada."); }
    }

    private static bool ValidConfiguration(ConfigurationDto x) => !string.IsNullOrWhiteSpace(x.Currency) && (!x.PickupEnabled || x.PublicPickupId is not null && x.PublicPickupId != Guid.Empty && x.Pickup is not null && ValidPickup(x.Pickup));
    private static bool ValidPickup(PickupDto x) => x.PublicPickupId != Guid.Empty && !string.IsNullOrWhiteSpace(x.DisplayName) && x.Address is not null && !string.IsNullOrWhiteSpace(x.Address.Street) && !string.IsNullOrWhiteSpace(x.Address.Number) && !string.IsNullOrWhiteSpace(x.Address.Neighborhood) && !string.IsNullOrWhiteSpace(x.Address.City) && !string.IsNullOrWhiteSpace(x.Address.State) && !string.IsNullOrWhiteSpace(x.Address.PostalCode);
    private static bool TryMap(
        ResponseDto x,
        IReadOnlyList<CartLine>? expectedLines,
        Guid? expectedPickupId,
        out CheckoutResponse? result)
    {
        result = null;
        if (x.PublicCheckoutId == Guid.Empty || string.IsNullOrWhiteSpace(x.Status) || x.Status.ToLowerInvariant() is not ("active" or "cancelled" or "expired") || x.ExpiresAt == default || x.AccessExpiresAt <= x.ExpiresAt || string.IsNullOrWhiteSpace(x.Currency) || x.Lines is null || x.Pickup is null || x.Contact is null || !ValidPickup(x.Pickup) || string.IsNullOrWhiteSpace(x.Contact.Name) || string.IsNullOrWhiteSpace(x.Contact.Email) || string.IsNullOrWhiteSpace(x.Contact.Phone) || x.Lines.Count == 0 || x.Lines.Any(l => l is null || l.PublicOfferId == Guid.Empty || l.Quantity is < 1 or > 10 || l.UnitPrice < 0 || l.LineTotal < 0 || l.LineTotal != l.UnitPrice * l.Quantity || !SafeImage(l.ImageUrl))) return false;
        if (x.Lines.GroupBy(x => x.PublicOfferId).Any(g => g.Count() != 1) || expectedLines is not null && (expectedLines.Count != x.Lines.Count || expectedLines.Any(expected => !x.Lines.Any(actual => actual.PublicOfferId == expected.PublicOfferId && actual.Quantity == expected.Quantity))) || expectedPickupId.HasValue && x.Pickup.PublicPickupId != expectedPickupId.Value || x.MerchandiseTotal < 0 || x.DiscountTotal < 0 || x.FreightTotal < 0 || x.Total < 0 || x.Total != x.MerchandiseTotal - x.DiscountTotal + x.FreightTotal || x.Lines.Sum(x => x.LineTotal) != x.MerchandiseTotal) return false;
        result = new() { PublicCheckoutId = x.PublicCheckoutId, Status = x.Status.ToLowerInvariant(), ExpiresAt = x.ExpiresAt, AccessExpiresAt = x.AccessExpiresAt, Currency = x.Currency, MerchandiseTotal = x.MerchandiseTotal, DiscountTotal = x.DiscountTotal, FreightTotal = x.FreightTotal, Total = x.Total, Pickup = Map(x.Pickup), Contact = new() { Name = x.Contact.Name ?? "", Email = x.Contact.Email ?? "", Phone = x.Contact.Phone ?? "" }, Lines = x.Lines.Select(l => new CheckoutLine { PublicOfferId = l.PublicOfferId, Quantity = l.Quantity, Presentation = l.Presentation ?? "", ImageUrl = l.ImageUrl, UnitPrice = l.UnitPrice, LineTotal = l.LineTotal }).ToList() };
        return true;
    }
    private static bool SafeImage(string? value) => string.IsNullOrWhiteSpace(value) || !value.TrimStart().StartsWith("//", StringComparison.Ordinal) && Uri.TryCreate(value.Trim(), UriKind.RelativeOrAbsolute, out var uri) && (value.TrimStart().StartsWith('/') || uri.IsAbsoluteUri && (uri.Scheme == "http" || uri.Scheme == "https"));
    private static PickupSnapshot Map(PickupDto x) => new() { PublicPickupId = x.PublicPickupId, DisplayName = x.DisplayName ?? "", Hours = x.Hours ?? "", Instructions = x.Instructions ?? "", Address = new() { Street = x.Address?.Street ?? "", Number = x.Address?.Number ?? "", Complement = x.Address?.Complement, Neighborhood = x.Address?.Neighborhood ?? "", City = x.Address?.City ?? "", State = x.Address?.State ?? "", PostalCode = x.Address?.PostalCode ?? "" } };
    private sealed record ReadResult<T>(HttpStatusCode? Status, CheckoutLoadState State, T? Value, string? Message);
    private sealed class CreateDto { public List<LineRequestDto> Lines { get; set; } = []; public ContactDto Contact { get; set; } = new(); public FulfillmentDto Fulfillment { get; set; } = new(); }
    private sealed class LineRequestDto { public Guid PublicOfferId { get; set; } public int Quantity { get; set; } }
    private sealed class ContactDto { public string? Name { get; set; } public string? Email { get; set; } public string? Phone { get; set; } }
    private sealed class FulfillmentDto { public string? Method { get; set; } public Guid PublicPickupId { get; set; } }
    private sealed class ConfigurationDto { public bool PickupEnabled { get; set; } public Guid? PublicPickupId { get; set; } public string? Currency { get; set; } public PickupDto? Pickup { get; set; } }
    private sealed class PickupDto { public Guid PublicPickupId { get; set; } public string? DisplayName { get; set; } public AddressDto? Address { get; set; } public string? Hours { get; set; } public string? Instructions { get; set; } }
    private sealed class AddressDto { public string? Street { get; set; } public string? Number { get; set; } public string? Complement { get; set; } public string? Neighborhood { get; set; } public string? City { get; set; } public string? State { get; set; } public string? PostalCode { get; set; } }
    private sealed class ResponseDto { public Guid PublicCheckoutId { get; set; } public string? Status { get; set; } public DateTimeOffset ExpiresAt { get; set; } public DateTimeOffset AccessExpiresAt { get; set; } public List<LineDto>? Lines { get; set; } public decimal MerchandiseTotal { get; set; } public decimal DiscountTotal { get; set; } public decimal FreightTotal { get; set; } public decimal Total { get; set; } public string? Currency { get; set; } public PickupDto? Pickup { get; set; } public ContactDto? Contact { get; set; } }
    private sealed class LineDto { public Guid PublicOfferId { get; set; } public int Quantity { get; set; } public string? Presentation { get; set; } public string? ImageUrl { get; set; } public decimal UnitPrice { get; set; } public decimal LineTotal { get; set; } }
}
