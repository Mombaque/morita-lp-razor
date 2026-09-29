using System.Globalization;

namespace Morita.LP.Razor.Models;

public static class CurrencyPresentation
{
    // Fixed separators keep "R$ 1.234,56" even when the host runs in invariant globalization mode.
    private static readonly NumberFormatInfo BrazilianNumbers = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = ".", NumberGroupSizes = [3] };

    public static string Symbol(string? currency) =>
        string.Equals(currency?.Trim(), "BRL", StringComparison.OrdinalIgnoreCase)
            ? "R$"
            : string.IsNullOrWhiteSpace(currency) ? "R$" : currency.Trim();

    public static string Format(decimal amount, string? currency) =>
        $"{Symbol(currency)} {amount.ToString("N2", BrazilianNumbers)}";

    public static string Format(decimal? amount, string? currency) =>
        amount is decimal value ? Format(value, currency) : "";

    public static string NormalizeFormattedPrice(string? formattedPrice)
    {
        if (string.IsNullOrWhiteSpace(formattedPrice)) return formattedPrice ?? "";

        var value = formattedPrice.Trim();
        return value.StartsWith("BRL", StringComparison.OrdinalIgnoreCase) && (value.Length == 3 || char.IsWhiteSpace(value[3]))
            ? $"R${value[3..]}"
            : value;
    }
}
