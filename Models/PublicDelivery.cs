using System.Text.Json.Serialization;

namespace Morita.LP.Razor.Models;

public sealed class PublicDelivery
{
    [JsonPropertyName("displayOrderNumber")] public string? DisplayOrderNumber { get; set; }
    [JsonPropertyName("orderNumber")] public string? OrderNumber { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("recipientFirstName")] public string? RecipientFirstName { get; set; }
    [JsonPropertyName("customerFirstName")] public string? CustomerFirstName { get; set; }
    [JsonPropertyName("customerName")] public string? CustomerName { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("district")] public string? District { get; set; }
    [JsonPropertyName("neighborhood")] public string? Neighborhood { get; set; }
    [JsonPropertyName("estimatedDeliveryAt")] public DateTimeOffset? EstimatedDeliveryAt { get; set; }
    [JsonPropertyName("expectedDeliveryAt")] public DateTimeOffset? ExpectedDeliveryAt { get; set; }
    [JsonPropertyName("estimatedDate")] public DateTimeOffset? EstimatedDate { get; set; }
    [JsonPropertyName("estimatedDeliveryFrom")] public DateTimeOffset? EstimatedDeliveryFrom { get; set; }
    [JsonPropertyName("estimatedDeliveryTo")] public DateTimeOffset? EstimatedDeliveryTo { get; set; }
    [JsonPropertyName("estimateFrom")] public DateTimeOffset? EstimateFrom { get; set; }
    [JsonPropertyName("estimateTo")] public DateTimeOffset? EstimateTo { get; set; }
    [JsonPropertyName("estimate")] public string? Estimate { get; set; }
    [JsonPropertyName("deliveryEstimate")] public string? DeliveryEstimate { get; set; }
    [JsonPropertyName("items")] public List<PublicDeliveryItem> Items { get; set; } = [];
    [JsonPropertyName("timeline")] public List<PublicDeliveryEvent> Timeline { get; set; } = [];
    [JsonPropertyName("events")] public List<PublicDeliveryEvent> Events { get; set; } = [];
    [JsonPropertyName("products")] public List<PublicDeliveryItem> Products { get; set; } = [];
    [JsonPropertyName("createdAt")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("confirmedAt")] public DateTimeOffset? ConfirmedAt { get; set; }
    [JsonPropertyName("statusUpdatedAt")] public DateTimeOffset? StatusUpdatedAt { get; set; }
    [JsonPropertyName("outForDeliveryAt")] public DateTimeOffset? OutForDeliveryAt { get; set; }
    [JsonPropertyName("deliveredAt")] public DateTimeOffset? DeliveredAt { get; set; }
    [JsonPropertyName("cancelledAt")] public DateTimeOffset? CancelledAt { get; set; }
    [JsonPropertyName("canceledAt")] public DateTimeOffset? CanceledAt { get; set; }
    [JsonPropertyName("destination")] public PublicDeliveryDestination? Destination { get; set; }
    [JsonPropertyName("destinationDistrict")] public string? DestinationDistrict { get; set; }
    [JsonPropertyName("destinationCity")] public string? DestinationCity { get; set; }
}

public sealed class PublicDeliveryDestination
{
    [JsonPropertyName("district")] public string? District { get; set; }
    [JsonPropertyName("neighborhood")] public string? Neighborhood { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
}

public sealed class PublicDeliveryItem
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("productName")] public string? ProductName { get; set; }
    [JsonPropertyName("quantity")] public int Quantity { get; set; } = 1;
    [JsonPropertyName("size")] public string? Size { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
}

public sealed class PublicDeliveryEvent
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("occurredAt")] public DateTimeOffset? OccurredAt { get; set; }
    [JsonPropertyName("isCurrent")] public bool IsCurrent { get; set; }
}
