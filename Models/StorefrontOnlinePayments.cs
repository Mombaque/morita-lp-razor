namespace Morita.LP.Razor.Models;

public static class StorefrontOnlinePayments
{
    public static bool TryParse(string? value, out OnlinePaymentMethod method)
    {
        foreach (var defined in Enum.GetValues<OnlinePaymentMethod>())
        {
            if (string.Equals(value, defined.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                method = defined;
                return true;
            }
        }

        method = default;
        return false;
    }

    public static IReadOnlyList<OnlinePaymentMethod> Normalize(IEnumerable<string>? methods)
    {
        if (methods is null)
        {
            return [];
        }

        var normalized = new List<OnlinePaymentMethod>(2);
        foreach (var method in methods)
        {
            if (TryParse(method, out var parsed) && !normalized.Contains(parsed))
            {
                normalized.Add(parsed);
            }
        }

        return normalized;
    }

    public static IReadOnlyList<OnlinePaymentMethod> Normalize(IEnumerable<OnlinePaymentMethod>? methods)
    {
        if (methods is null)
        {
            return [];
        }

        var normalized = new List<OnlinePaymentMethod>(2);
        foreach (var method in methods)
        {
            if (!normalized.Contains(method))
            {
                normalized.Add(method);
            }
        }

        return normalized;
    }

    public static OnlinePaymentMethod? DefaultMethod(IReadOnlyList<OnlinePaymentMethod> methods)
    {
        if (methods.Contains(OnlinePaymentMethod.Pix))
        {
            return OnlinePaymentMethod.Pix;
        }

        return methods.Contains(OnlinePaymentMethod.Card) ? OnlinePaymentMethod.Card : null;
    }

    public static string ContinueLabel(OnlinePaymentMethod? method) =>
        method == OnlinePaymentMethod.Card ? "Continuar para o cartão" : "Continuar para o PIX";

    public static string ToWireValue(this OnlinePaymentMethod method) =>
        method.ToString().ToLowerInvariant();
}
