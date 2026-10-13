using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public interface IReviewClient
{
    Task<ReviewFormResult> GetFormAsync(string token, CancellationToken cancellationToken = default);
    Task<ReviewSubmitResult> SubmitAsync(string token, IReadOnlyList<ReviewInput> items, CancellationToken cancellationToken = default);
    Task<ProductReviewPage?> GetProductReviewsAsync(string slug, int page, CancellationToken cancellationToken = default);
    Task<ReviewOptOutState> OptOutAsync(string token, CancellationToken cancellationToken = default) => Task.FromResult(ReviewOptOutState.Unavailable);
}

public sealed partial class ReviewClient(HttpClient httpClient, IOptions<CatalogApiOptions> options, ILogger<ReviewClient> logger) : IReviewClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsValidToken(string? token) => token is not null && TokenPattern().IsMatch(token);

    public static bool IsValidOptOutToken(string? token) => token is not null && OptOutTokenPattern().IsMatch(token);

    public async Task<ReviewFormResult> GetFormAsync(string token, CancellationToken cancellationToken = default)
    {
        if (!IsValidToken(token))
            return new(ReviewLoadState.NotFound, null);
        var (status, form) = await SendAsync<ReviewForm>(HttpMethod.Get, $"v1/storefront/reviews/{Uri.EscapeDataString(token)}", null, cancellationToken);
        if (status == HttpStatusCode.NotFound)
            return new(ReviewLoadState.NotFound, null);
        if (status != HttpStatusCode.OK || form is null || !IsValidForm(form))
            return new(ReviewLoadState.Unavailable, null);
        return new(ReviewLoadState.Success, form);
    }

    public async Task<ReviewSubmitResult> SubmitAsync(string token, IReadOnlyList<ReviewInput> items, CancellationToken cancellationToken = default)
    {
        if (!IsValidToken(token))
            return new(ReviewSubmitState.NotFound, null);
        var body = new { items = items.Select(item => new { slug = item.Slug, rating = item.Rating, comment = item.Comment }) };
        var (status, messages) = await SendAsync<List<NotificationResponse>>(HttpMethod.Post, $"v1/storefront/reviews/{Uri.EscapeDataString(token)}", body, cancellationToken);
        return status switch
        {
            HttpStatusCode.Accepted or HttpStatusCode.OK => new(ReviewSubmitState.Accepted, null),
            HttpStatusCode.NotFound => new(ReviewSubmitState.NotFound, null),
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict => new(ReviewSubmitState.Invalid, messages?.Select(message => message.Message).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text) && text.Length <= 200)),
            _ => new(ReviewSubmitState.Unavailable, null)
        };
    }

    public async Task<ProductReviewPage?> GetProductReviewsAsync(string slug, int page, CancellationToken cancellationToken = default)
    {
        var (status, result) = await SendAsync<ProductReviewPage>(HttpMethod.Get, $"v1/storefront/catalog/products/{Uri.EscapeDataString(slug.Trim())}/reviews?page={Math.Max(1, page)}", null, cancellationToken);
        return status == HttpStatusCode.OK && result is not null && IsValidPage(result) ? result : null;
    }

    public async Task<ReviewOptOutState> OptOutAsync(string token, CancellationToken cancellationToken = default)
    {
        if (!IsValidOptOutToken(token))
            return ReviewOptOutState.Invalid;
        var (status, _) = await SendAsync<JsonElement>(HttpMethod.Post, "v1/storefront/review-requests/opt-out", new { token }, cancellationToken);
        return status switch
        {
            HttpStatusCode.NoContent or HttpStatusCode.OK => ReviewOptOutState.Done,
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest => ReviewOptOutState.Invalid,
            _ => ReviewOptOutState.Unavailable
        };
    }

    private async Task<(HttpStatusCode? Status, T? Value)> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.TimeoutSeconds, 1, 30)));
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = JsonContent.Create(body, options: JsonOptions);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.Content.Headers.ContentLength is > 262_144)
                return (null, default);
            var value = response.Content.Headers.ContentType?.MediaType == "application/json"
                ? await response.Content.ReadFromJsonAsync<T>(JsonOptions, timeout.Token)
                : default;
            return (response.StatusCode, value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { logger.LogWarning("Review request timed out"); return (null, default); }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "Review request unavailable"); return (null, default); }
        catch (JsonException ex) { logger.LogWarning(ex, "Review response was invalid"); return (null, default); }
    }

    private static bool IsValidForm(ReviewForm form) =>
        form.Items is { Count: > 0 and <= 50 } &&
        form.Items.All(item => item is not null && item.Slug.Length is > 0 and <= 200 && item.Name.Length is > 0 and <= 300 && (item.ImageUrl is null || IsCatalogImage(item.ImageUrl)));

    private static bool IsValidPage(ProductReviewPage page) =>
        page.Rating is not null && page.Items is not null && page.Items.Count <= 50 &&
        page.Items.All(item => item is not null && item.Rating is >= 1 and <= 5 && item.DisplayName.Length is > 0 and <= 60 && item.Comment.Length <= 1000 && (item.StaffReply?.Length ?? 0) <= 1000);

    private static bool IsCatalogImage(string url) => url.StartsWith("/v1/storefront/catalog/images/", StringComparison.Ordinal) && url.Length <= 100;

    [GeneratedRegex("^[A-Z0-9-]{1,19}\\.[A-Za-z0-9_-]{20,64}$")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{43}\\.[A-Za-z0-9_-]{43}$")]
    private static partial Regex OptOutTokenPattern();

    private sealed class NotificationResponse
    {
        public string? Message { get; set; }
    }
}
