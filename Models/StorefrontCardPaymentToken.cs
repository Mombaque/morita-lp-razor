namespace Morita.LP.Razor.Models;

public static class StorefrontCardPaymentToken
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 64;

    public static bool TryNormalize(string? value, out string token)
    {
        token = value?.Trim() ?? string.Empty;
        if (token.Length is < MinimumLength or > MaximumLength)
        {
            return false;
        }

        return !LooksLikePrimaryAccountNumber(token);
    }

    public static bool LooksLikePrimaryAccountNumber(string value)
    {
        var compact = new string(value.Where(character => !char.IsWhiteSpace(character) && character != '-').ToArray());
        return compact.Length is >= 12 and <= 19 && compact.All(char.IsDigit);
    }
}
