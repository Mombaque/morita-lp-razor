namespace Morita.LP.Razor.Configuration;

public sealed class DeliveryTrackingOptions
{
    public const string Section = "DeliveryTracking";
    public const string DefaultTimeZoneId = "America/Sao_Paulo";

    public string ApiBaseUrl { get; set; } = string.Empty;
    public string PublicDeliveryPath { get; set; } = "/v1/public/deliveries/{publicToken}";
    public string Host { get; set; } = "moritafight.com.br";
    public string GoogleReviewUrl { get; set; } = string.Empty;
    public string CatalogUrl { get; set; } = "https://moritafight.com.br/";
    public string WhatsAppUrl { get; set; } = "https://wa.me/c/5515981079332";
    public string InstagramUrl { get; set; } = "https://www.instagram.com/morita.fight/";
    public string ProxySecret { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    public static bool IsValidApiBaseUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));

    public static bool IsValidTimeZoneId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(value);
            return true;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }

    public static bool IsValidPublicDeliveryPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("/", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal))
            return false;
        if (!Uri.TryCreate(value, UriKind.Relative, out _)) return false;
        const string placeholder = "{publicToken}";
        return value.IndexOf(placeholder, StringComparison.Ordinal) >= 0 &&
               value.Split(placeholder, StringSplitOptions.None).Length == 2 &&
               !value.Contains("?", StringComparison.Ordinal) &&
               !value.Contains("#", StringComparison.Ordinal);
    }
}
