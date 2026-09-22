namespace Morita.LP.Razor.Models;

public static class StorefrontHostedCheckoutUrl
{
    public const int MaximumLength = 2048;

    public static bool IsAllowed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumLength)
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps
            || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
    }
}
