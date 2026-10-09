namespace Morita.LP.Razor.Models;

public static class EmbeddedCardChallenge
{
    public const int MaximumCreqLength = 8192;

    public static bool IsAllowed(string? url, string? creq) =>
        !string.IsNullOrWhiteSpace(url)
        && url.Length <= StorefrontHostedCheckoutUrl.MaximumLength
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.Fragment)
        && !string.IsNullOrWhiteSpace(creq)
        && creq.Length <= MaximumCreqLength;
}
