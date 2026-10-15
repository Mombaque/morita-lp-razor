namespace Morita.LP.Razor.Models;

public static class AccountPresentation
{
    public static string PaymentStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "converted" or "approved" => "Pagamento aprovado",
        "processing" or "conversionpending" => "Pagamento em análise",
        "failed" => "Pagamento não aprovado",
        "cancelled" => "Pagamento cancelado",
        "expired" => "Pagamento expirado",
        "refundpending" => "Estorno em andamento",
        "refunded" => "Estornado",
        _ => "Pagamento pendente"
    };

    public static string PaymentStatusClass(string? status) => PaymentStatus(status) switch
    {
        "Pagamento pendente" => "payment-status-pending",
        "Pagamento cancelado" => "payment-status-cancelled",
        _ => ""
    };

    public static string FulfillmentStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "pending" => "Aguardando preparação",
        "preparingpickup" => "Em preparação",
        "readyforpickup" => "Pronto para retirada",
        "pickedup" => "Retirado",
        "awaitinglabel" or "labelpurchasepending" => "Preparando envio",
        "labelpurchased" => "Etiqueta emitida",
        "intransit" => "Em trânsito",
        "delivered" => "Entregue",
        "cancellationpending" => "Cancelamento em andamento",
        "cancelled" => "Cancelado",
        "exception" => "Ocorrência em verificação",
        _ => "Em atualização"
    };

    public static string FulfillmentStatus(string? method, string? status) =>
        string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase)
            ? string.Equals(method, "pickup", StringComparison.OrdinalIgnoreCase) ? "Em preparação" : "Preparando envio"
            : FulfillmentStatus(status);

    public static string? NextStep(string? method, string? paymentStatus, string? fulfillmentStatus)
    {
        if (paymentStatus?.ToLowerInvariant() is not ("converted" or "approved")) return null;
        var pickup = string.Equals(method, "pickup", StringComparison.OrdinalIgnoreCase);
        return fulfillmentStatus?.ToLowerInvariant() switch
        {
            "pending" or "preparingpickup" when pickup => "Próximo passo: avisaremos por e-mail quando o pedido estiver pronto para retirada. Não é preciso vir à loja antes disso.",
            "pending" or "awaitinglabel" or "labelpurchasepending" or "labelpurchased" when !pickup => "Próximo passo: avisaremos por e-mail com o código de rastreio assim que o pedido for despachado.",
            _ => null
        };
    }

    public static string CodeExpiry(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt is not { } expiry) return "O código vale por alguns minutos.";
        var minutes = Math.Max(1, (int)Math.Ceiling((expiry - now).TotalMinutes));
        return $"O código vale por {minutes} {(minutes == 1 ? "minuto" : "minutos")} (até {TimePresentation.ToStoreTime(expiry):HH:mm}).";
    }

    public static bool HasPickupText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Any(char.IsLetterOrDigit);

    public static string EffectiveFulfillmentStatus(string? method, string? fulfillmentStatus, string? shipmentStatus) =>
        string.Equals(method, "shipping", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(fulfillmentStatus, "cancelled", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(shipmentStatus)
            ? shipmentStatus.ToLowerInvariant()
            : fulfillmentStatus?.ToLowerInvariant() ?? "";

    public static string EffectiveFulfillmentStatus(PublicOrder order) =>
        EffectiveFulfillmentStatus(order.FulfillmentMethod, order.FulfillmentStatus, order.Shipment?.Status);

    public static string FulfillmentMethod(string? method) => method?.ToLowerInvariant() switch
    {
        "shipping" => "Entrega",
        "pickup" => "Retirada na Morita",
        _ => "Entrega"
    };

    public static string ProblemReason(string? reason) => reason?.ToLowerInvariant() switch
    {
        "damaged" => "Item danificado",
        "exchange" => "Quero trocar",
        "return" => "Quero devolver",
        "other" => "Outro",
        _ => "Outro"
    };
}
