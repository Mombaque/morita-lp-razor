namespace Morita.LP.Razor.Models;

public static class CurrencyPresentation
{
    public static string Symbol(string? currency) =>
        string.Equals(currency?.Trim(), "BRL", StringComparison.OrdinalIgnoreCase)
            ? "R$"
            : string.IsNullOrWhiteSpace(currency) ? "R$" : currency.Trim();

    public static string NormalizeFormattedPrice(string? formattedPrice)
    {
        if (string.IsNullOrWhiteSpace(formattedPrice)) return formattedPrice ?? "";

        var value = formattedPrice.Trim();
        return value.StartsWith("BRL", StringComparison.OrdinalIgnoreCase) && (value.Length == 3 || char.IsWhiteSpace(value[3]))
            ? $"R${value[3..]}"
            : value;
    }
}
