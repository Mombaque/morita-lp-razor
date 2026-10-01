using System.ComponentModel.DataAnnotations;

namespace Morita.LP.Razor.Models;

// CPF or CNPJ of the buyer on a nota fiscal. Mirrors the API rules (modulo 11 check digits; CNPJ letters
// in the first twelve characters since July 2026) so the form rejects typos before calling the API.
public static class BuyerDocument
{
    private static readonly int[] CnpjFirstWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] CnpjSecondWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    public static string Normalize(string? value) =>
        new((value ?? "").Where(character => character is not ('.' or '/' or '-') && !char.IsWhiteSpace(character)).Select(char.ToUpperInvariant).ToArray());

    public static bool IsValid(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 11 ? IsValidCpf(normalized) : IsValidCnpj(normalized);
    }

    public static string Format(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 11) return $"{normalized[..3]}.{normalized[3..6]}.{normalized[6..9]}-{normalized[9..]}";
        if (normalized.Length == 14) return $"{normalized[..2]}.{normalized[2..5]}.{normalized[5..8]}/{normalized[8..12]}-{normalized[12..]}";
        return value?.Trim() ?? "";
    }

    private static bool IsValidCpf(string digits)
    {
        if (!digits.All(char.IsAsciiDigit) || digits.All(character => character == digits[0])) return false;
        return digits[9] - '0' == CpfCheckDigit(digits[..9]) && digits[10] - '0' == CpfCheckDigit(digits[..10]);
    }

    private static int CpfCheckDigit(string digits)
    {
        var sum = digits.Select((digit, index) => (digit - '0') * (digits.Length + 1 - index)).Sum();
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    private static bool IsValidCnpj(string value)
    {
        if (value.Length != 14 || !value[..12].All(char.IsAsciiLetterOrDigit) || !value[12..].All(char.IsAsciiDigit) || value.All(character => character == value[0])) return false;
        return value[12] - '0' == CnpjCheckDigit(value[..12], CnpjFirstWeights) && value[13] - '0' == CnpjCheckDigit(value[..13], CnpjSecondWeights);
    }

    // Each character is worth its ASCII code minus 48, so digits keep their value.
    private static int CnpjCheckDigit(string characters, int[] weights)
    {
        var sum = characters.Select((character, index) => (character - '0') * weights[index]).Sum();
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class BuyerDocumentAttribute : ValidationAttribute
{
    public BuyerDocumentAttribute() : base("Informe um CPF ou CNPJ válido.") { }

    public override bool IsValid(object? value) => value is not string text || string.IsNullOrWhiteSpace(text) || BuyerDocument.IsValid(text);
}
