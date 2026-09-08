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

    public static string FulfillmentMethod(string? method) => method?.ToLowerInvariant() switch
    {
        "shipping" => "Entrega",
        "pickup" => "Retirada na Morita",
        _ => "Entrega"
    };
}
