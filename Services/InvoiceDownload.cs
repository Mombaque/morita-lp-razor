using System.Net;
using Morita.LP.Razor.Models;

namespace Morita.LP.Razor.Services;

public static class InvoiceDownload
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedContentTypes = ["application/pdf", "application/xml", "text/xml"];

    public static async Task<InvoiceFile?> SendAsync(HttpClient httpClient, HttpRequestMessage request, int timeoutSeconds, ILogger logger, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 30)));
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
                return null;
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase) || response.Content.Headers.ContentLength > MaximumBytes)
                return null;
            var content = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            if (content.Length is 0 or > MaximumBytes)
                return null;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            return new InvoiceFile(content, contentType, string.IsNullOrWhiteSpace(fileName) ? DefaultName(contentType) : Path.GetFileName(fileName));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Invoice download timed out");
            return null;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Invoice download unavailable");
            return null;
        }
    }

    private static string DefaultName(string contentType) => contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ? "nota-fiscal.pdf" : "nota-fiscal.xml";
}
