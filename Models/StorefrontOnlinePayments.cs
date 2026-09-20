namespace Morita.LP.Razor.Models;

public static class StorefrontOnlinePayments
{
    public const string Pix = "pix";
    public const string Card = "card";

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? methods)
    {
        if (methods is null)
        {
            return [];
        }

        var normalized = new List<string>(2);
        foreach (var method in methods)
        {
            if (IsPix(method) && !normalized.Contains(Pix, StringComparer.Ordinal))
            {
                normalized.Add(Pix);
            }
            else if (IsCard(method) && !normalized.Contains(Card, StringComparer.Ordinal))
            {
                normalized.Add(Card);
            }
        }

        return normalized;
    }

    public static string DefaultMethod(IReadOnlyList<string> methods)
    {
        if (methods.Contains(Pix, StringComparer.Ordinal))
        {
            return Pix;
        }

        return methods.Contains(Card, StringComparer.Ordinal) ? Card : "";
    }

    public static bool IsPix(string? value) =>
        string.Equals(value, Pix, StringComparison.OrdinalIgnoreCase);

    public static bool IsCard(string? value) =>
        string.Equals(value, Card, StringComparison.OrdinalIgnoreCase);

    public static string ContinueLabel(string? method) =>
        IsCard(method) ? "Continuar para o cartão" : "Continuar para o PIX";
}
