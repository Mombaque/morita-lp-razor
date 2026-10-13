namespace Morita.LP.Razor.Models;

public enum OrderTimelineStepState { Done, Current, Upcoming }

public sealed record OrderTimelineStep(string Label, OrderTimelineStepState State, DateTimeOffset? At);

public static class OrderTimeline
{
    public const string TrackingSiteUrl = "https://melhorrastreio.com.br/";

    private static readonly string[] ShippingSteps = ["Pedido confirmado", "Preparando envio", "Etiqueta emitida", "Em trânsito", "Entregue"];
    private static readonly string[] PickupSteps = ["Pedido confirmado", "Em preparação", "Pronto para retirada", "Retirado"];

    public static IReadOnlyList<OrderTimelineStep> Build(PublicOrder order)
    {
        if (!IsPaid(order.PaymentStatus))
            return [];
        var pickup = string.Equals(order.FulfillmentMethod, "pickup", StringComparison.OrdinalIgnoreCase);
        var status = AccountPresentation.EffectiveFulfillmentStatus(order);
        var reached = pickup ? PickupIndex(status) : ShippingIndex(status);
        if (reached is null)
            return [];
        var labels = pickup ? PickupSteps : ShippingSteps;
        var final = reached.Value == labels.Length - 1;
        return labels
            .Select((label, index) => new OrderTimelineStep(
                label,
                index < reached.Value || (final && index == reached.Value)
                    ? OrderTimelineStepState.Done
                    : index == reached.Value ? OrderTimelineStepState.Current : OrderTimelineStepState.Upcoming,
                index <= reached.Value ? StepTime(order, pickup, index, labels.Length) : null))
            .ToList();
    }

    public static bool HasException(PublicOrder order) =>
        AccountPresentation.EffectiveFulfillmentStatus(order) == "exception";

    private static bool IsPaid(string? status) =>
        status?.ToLowerInvariant() is "converted" or "approved";

    private static int? ShippingIndex(string? status) => status?.ToLowerInvariant() switch
    {
        "pending" or "awaitinglabel" or "labelpurchasepending" => 1,
        "labelpurchased" => 2,
        "intransit" or "exception" => 3,
        "delivered" => 4,
        _ => null
    };

    private static int? PickupIndex(string? status) => status?.ToLowerInvariant() switch
    {
        "pending" or "preparingpickup" => 1,
        "readyforpickup" => 2,
        "pickedup" => 3,
        _ => null
    };

    private static DateTimeOffset? StepTime(PublicOrder order, bool pickup, int index, int count)
    {
        if (index == 0)
            return order.CreatedAt;
        if (pickup)
            return index == count - 1 ? order.FulfillmentUpdatedAt : null;
        return index switch
        {
            3 => order.Shipment?.ShippedAt,
            4 => order.Shipment?.DeliveredAt,
            _ => null
        };
    }
}
