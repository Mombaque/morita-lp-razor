namespace Morita.LP.Razor.Models;

public sealed class StorefrontStore
{
    public string DisplayName { get; init; } = "";
    public string? LegalName { get; init; }
    public string? Cnpj { get; init; }
    public string? ContactEmail { get; init; }
    public string? ContactPhone { get; init; }
    public StorefrontStoreAddress? Address { get; init; }
    public bool CheckoutPaused { get; init; }
    public string? CheckoutPausedMessage { get; init; }

    public const string DefaultCheckoutPausedMessage =
        "As compras pelo site estão pausadas no momento. Fale com a loja pelo WhatsApp para finalizar seu pedido.";

    public string PausedMessage => string.IsNullOrWhiteSpace(CheckoutPausedMessage) ? DefaultCheckoutPausedMessage : CheckoutPausedMessage;

    public bool HasLegalIdentity => !string.IsNullOrWhiteSpace(LegalName) && !string.IsNullOrWhiteSpace(Cnpj);
}

public sealed class StorefrontStoreAddress
{
    public string Street { get; init; } = "";
    public string Number { get; init; } = "";
    public string? Complement { get; init; }
    public string Neighborhood { get; init; } = "";
    public string City { get; init; } = "";
    public string State { get; init; } = "";
    public string PostalCode { get; init; } = "";

    public string Line
    {
        get
        {
            var street = string.Join(", ", new[] { Street, Number, Complement }.Where(part => !string.IsNullOrWhiteSpace(part)));
            var city = string.Join(" - ", new[] { City, State }.Where(part => !string.IsNullOrWhiteSpace(part)));
            return string.Join(" · ", new[] { street, Neighborhood, city, FormattedPostalCode }.Where(part => !string.IsNullOrWhiteSpace(part)));
        }
    }

    public string FormattedPostalCode
    {
        get
        {
            var digits = new string(PostalCode.Where(char.IsAsciiDigit).ToArray());
            return digits.Length == 8 ? $"{digits[..5]}-{digits[5..]}" : PostalCode.Trim();
        }
    }
}
