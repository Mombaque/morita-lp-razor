using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public interface IPublicAssistantClient
{
    Task<PublicAssistantResult<(PublicAssistantSession Session, string AccessToken)>> CreateSessionAsync(CreatePublicAssistantSessionRequest request, CancellationToken cancellationToken = default);
    Task<PublicAssistantResult<PublicAssistantSession>> GetSessionAsync(CancellationToken cancellationToken = default);
    Task<PublicAssistantResult<PublicAssistantTurn>> SendMessageAsync(PublicAssistantMessageRequest request, CancellationToken cancellationToken = default);
    Task<PublicAssistantResult<PublicAssistantSubmission>> SubmitAsync(PublicAssistantSubmitRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<PublicAssistantActionResult> CloseAsync(CancellationToken cancellationToken = default);
}

public sealed class PublicAssistantClient(
    HttpClient httpClient,
    IOptions<CatalogApiOptions> options,
    IOptions<StorefrontOptions> storefrontOptions,
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment environment,
    IPublicAssistantCookieStore cookieStore,
    ILogger<PublicAssistantClient> logger) : IPublicAssistantClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CatalogApiOptions apiOptions = options.Value;
    private readonly int timeoutSeconds = storefrontOptions.Value.PublicAssistantTimeoutSeconds;

    public async Task<PublicAssistantResult<(PublicAssistantSession Session, string AccessToken)>> CreateSessionAsync(CreatePublicAssistantSessionRequest request, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<PublicAssistantSession>(HttpMethod.Post, "v1/storefront/assistant/sessions", request, null, cancellationToken, includeCredential: false);
        if (!result.IsSuccess || result.Value is null) return Failure<(PublicAssistantSession, string)>(result);
        if (string.IsNullOrWhiteSpace(result.Value.AccessToken) || !ValidSession(result.Value, expectAccessToken: true)) return Failure<(PublicAssistantSession, string)>(result, PublicAssistantFailureKind.Malformed);
        var token = result.Value.AccessToken;
        result.Value.AccessToken = null;
        return new(PublicAssistantFailureKind.None, (result.Value, token));
    }

    public async Task<PublicAssistantResult<PublicAssistantSession>> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        var credentials = cookieStore.Read();
        if (credentials is null) return new(PublicAssistantFailureKind.NotFound, default);
        var result = await SendAsync<PublicAssistantSession>(HttpMethod.Get, $"v1/storefront/assistant/sessions/{credentials.PublicId:D}", null, null, cancellationToken);
        if (!result.IsSuccess || result.Value is null) return result;
        if (!ValidSession(result.Value, expectAccessToken: false)) return new(PublicAssistantFailureKind.Malformed, default, "A resposta do assistente não pôde ser validada.");
        return result;
    }

    public async Task<PublicAssistantResult<PublicAssistantTurn>> SendMessageAsync(PublicAssistantMessageRequest request, CancellationToken cancellationToken = default)
    {
        var credentials = cookieStore.Read();
        if (credentials is null) return new(PublicAssistantFailureKind.NotFound, default);
        var result = await SendAsync<PublicAssistantTurn>(HttpMethod.Post, $"v1/storefront/assistant/sessions/{credentials.PublicId:D}/messages", request, null, cancellationToken);
        return result.IsSuccess && (result.Value is null || !ValidTurn(result.Value))
            ? new(PublicAssistantFailureKind.Malformed, default, "A resposta do assistente não pôde ser validada.")
            : result;
    }

    public async Task<PublicAssistantResult<PublicAssistantSubmission>> SubmitAsync(PublicAssistantSubmitRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var credentials = cookieStore.Read();
        if (credentials is null) return new(PublicAssistantFailureKind.NotFound, default);
        var result = await SendAsync<PublicAssistantSubmission>(HttpMethod.Post, $"v1/storefront/assistant/sessions/{credentials.PublicId:D}/submit", request, ("Idempotency-Key", idempotencyKey), cancellationToken);
        return result.IsSuccess && (result.Value is null || result.Value.CustomerProductRequestId <= 0 || !result.Value.Received)
            ? new(PublicAssistantFailureKind.Malformed, default, "A resposta do assistente não pôde ser validada.")
            : result;
    }

    public async Task<PublicAssistantActionResult> CloseAsync(CancellationToken cancellationToken = default)
    {
        var credentials = cookieStore.Read();
        if (credentials is null) return new(PublicAssistantFailureKind.NotFound);
        var result = await SendAsync<object>(HttpMethod.Post, $"v1/storefront/assistant/sessions/{credentials.PublicId:D}/close", null, null, cancellationToken);
        return new(result.Failure, result.Message);
    }

    private async Task<PublicAssistantResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, (string Name, string Value)? header, CancellationToken callerToken, bool includeCredential = true)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 60)));
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
            if (header is not null) request.Headers.TryAddWithoutValidation(header.Value.Name, header.Value.Value);
            if (includeCredential && cookieStore.Read() is { } credentials)
                request.Headers.TryAddWithoutValidation("X-Assistant-Token", credentials.AccessToken);
            if (httpContextAccessor.HttpContext is { } context)
                request.Headers.TryAddWithoutValidation("X-Morita-Client-IP", ClientIdentityResolver.Resolve(context, environment));
            if (!string.IsNullOrWhiteSpace(apiOptions.ProxySecret))
                request.Headers.TryAddWithoutValidation("X-Morita-Proxy-Secret", apiOptions.ProxySecret);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NoContent) return new(PublicAssistantFailureKind.None, typeof(T) == typeof(object) ? (T)(object)new object() : default);
            if (response.StatusCode == HttpStatusCode.NotFound) return new(PublicAssistantFailureKind.NotFound, default, "A conversa não foi encontrada.");
            if (response.StatusCode == HttpStatusCode.Conflict) return new(PublicAssistantFailureKind.Conflict, default, "A conversa foi atualizada. Recarregue os dados e tente novamente.");
            if (response.StatusCode == HttpStatusCode.Gone) return new(PublicAssistantFailureKind.Expired, default, "Esta conversa expirou. Inicie uma nova conversa.");
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity) return new(PublicAssistantFailureKind.Validation, default, await ReadMessageAsync(response, "Revise os dados informados.", timeout.Token));
            if (response.StatusCode == (HttpStatusCode)429) return new(PublicAssistantFailureKind.RateLimited, default, "Muitas tentativas. Aguarde um pouco e tente novamente.");
            if ((int)response.StatusCode >= 500) return new(PublicAssistantFailureKind.Unavailable, default, "O assistente está temporariamente indisponível.");
            if (!response.IsSuccessStatusCode) return new(PublicAssistantFailureKind.Malformed, default, "A resposta do assistente não pôde ser validada.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, timeout.Token);
            if (value is null) return new(PublicAssistantFailureKind.Malformed, default, "A resposta do assistente não pôde ser validada.");
            return new(PublicAssistantFailureKind.None, value);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            logger.LogWarning("Public assistant request timed out");
            return new(PublicAssistantFailureKind.Timeout, default, "O atendimento demorou. Seus dados foram preservados; tente novamente.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Public assistant API unavailable");
            return new(PublicAssistantFailureKind.Unavailable, default, "Não foi possível acessar o assistente agora.");
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Public assistant response malformed");
            return new(PublicAssistantFailureKind.Malformed, default, "A resposta do assistente não pôde ser validada.");
        }
    }

    private static async Task<string> ReadMessageAsync(HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
    {
        try
        {
            var messages = await response.Content.ReadFromJsonAsync<List<string>>(JsonOptions, cancellationToken);
            return messages is { Count: > 0 } ? string.Join(" ", messages) : fallback;
        }
        catch (JsonException) { return fallback; }
    }

    private static bool ValidSession(PublicAssistantSession session, bool expectAccessToken)
    {
        if (session.PublicId == Guid.Empty || session.ExpiresAt <= DateTime.UtcNow.AddMinutes(-1) || session.ExpiresAt > DateTime.UtcNow.AddDays(31) || session.Status is not ("Active" or "AwaitingConfirmation" or "Submitted") || session.DraftRevision < 0 || session.Draft is null || session.Messages is null || session.Messages.Count > 41 || session.Messages.Any(message => message is null || message.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(message.Content) || message.Content.Length > 10000) || !ValidDraft(session.Draft) || !ValidAction(session.ActionType, session.Summary, session.ConfirmationToken)) return false;
        return expectAccessToken
            ? session.AccessToken is { Length: >= 32 and <= 200 }
            : session.AccessToken is null;
    }

    private static bool ValidTurn(PublicAssistantTurn turn) =>
        turn.Message is not null &&
        turn.Message.Role == "assistant" &&
        !string.IsNullOrWhiteSpace(turn.Message.Content) &&
        turn.Message.Content.Length <= 10000 &&
        turn.Draft is not null &&
        ValidDraft(turn.Draft) &&
        turn.DraftRevision >= 0 &&
        turn.CatalogProducts is not null &&
        turn.CatalogProducts.Count <= 6 &&
        turn.CatalogProducts.All(ValidCard) &&
        ValidAction(turn.ActionType, turn.Summary, turn.ConfirmationToken);

    private static bool ValidDraft(JsonDocument draft)
    {
        if (draft.RootElement.ValueKind != JsonValueKind.Object || !draft.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return false;
        return items.GetArrayLength() <= 10;
    }

    private static bool ValidCard(PublicAssistantCatalogCard card)
    {
        if (card is null || card.Source is not ("PublishedCatalog" or "AssistantConfigured") || string.IsNullOrWhiteSpace(card.Name) || card.Name.Length > 300 || card.AvailableSizes is null || card.AvailableColors is null || card.AvailableSizes.Count > 30 || card.AvailableColors.Count > 30) return false;
        if (card.Source == "AssistantConfigured") return card.ProductPageUrl is null && card.Price is null && card.Currency is null && card.Available is null;
        return !string.IsNullOrWhiteSpace(card.Slug) &&
            card.ProductPageUrl is not null &&
            card.ProductPageUrl.StartsWith("/products/", StringComparison.Ordinal) &&
            !card.ProductPageUrl.StartsWith("//", StringComparison.Ordinal) &&
            (card.Price is null && card.Currency is null || card.Price is >= 0 and <= 100000000 && card.Currency is { Length: 3 });
    }

    private static bool ValidAction(string actionType, string? summary, string? confirmationToken) => actionType switch
    {
        "None" => summary is null && confirmationToken is null,
        "Confirmation" => summary is { Length: > 0 and <= 10000 } && confirmationToken is { Length: >= 32 and <= 500 },
        _ => false
    };

    private static PublicAssistantResult<T> Failure<T>(PublicAssistantResult<PublicAssistantSession> result, PublicAssistantFailureKind? overrideFailure = null) => new(overrideFailure ?? result.Failure, default, overrideFailure is null ? result.Message : "A resposta do assistente não pôde ser validada.");
}
