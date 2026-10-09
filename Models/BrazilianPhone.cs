namespace Morita.LP.Razor.Models;

public static class BrazilianPhone
{
    public static string Format(string? value)
    {
        var digits = new string((value ?? "").Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is 12 or 13 && digits.StartsWith("55", StringComparison.Ordinal)) digits = digits[2..];
        return digits.Length switch
        {
            11 => $"({digits[..2]}) {digits[2..7]}-{digits[7..]}",
            10 => $"({digits[..2]}) {digits[2..6]}-{digits[6..]}",
            _ => value?.Trim() ?? ""
        };
    }
}
