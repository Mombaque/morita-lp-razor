namespace Morita.LP.Razor.Models;

public sealed class EmbeddedCardRequest
{
    public string? Token { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? IssuerId { get; set; }
    public int? Installments { get; set; }
    public string? PayerEmail { get; set; }
    public string? IdentificationType { get; set; }
    public string? IdentificationNumber { get; set; }

    public bool TryToPayment(out EmbeddedCardPayment? payment)
    {
        payment = null;
        var token = Token?.Trim();
        var methodId = PaymentMethodId?.Trim().ToLowerInvariant();
        var issuerId = string.IsNullOrWhiteSpace(IssuerId) ? null : IssuerId.Trim();
        var email = PayerEmail?.Trim();
        var documentType = IdentificationType?.Trim().ToUpperInvariant();
        var document = new string((IdentificationNumber ?? "").Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        if (token is not { Length: >= 8 and <= 200 } || !token.All(IsTokenCharacter)
            || methodId is not { Length: >= 2 and <= 40 } || !methodId.All(IsTokenCharacter)
            || issuerId is { Length: > 40 } || issuerId is not null && !issuerId.All(char.IsAsciiDigit)
            || Installments is not (>= 1 and <= 24)
            || email is not { Length: >= 3 and <= 254 } || !email.Contains('@')
            || documentType is not ("CPF" or "CNPJ")
            || document.Length is not (11 or 14))
            return false;

        payment = new(token, methodId, issuerId, Installments.Value, email, documentType, document);
        return true;
    }

    private static bool IsTokenCharacter(char value) => char.IsAsciiLetterOrDigit(value) || value is '_' or '-';
}
