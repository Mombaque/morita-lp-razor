using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Morita.LP.Razor.Configuration;
using Morita.LP.Razor.Models;
using Morita.LP.Razor.Services;

namespace Morita.LP.Razor.Pages;

public sealed class EntregaModel(IPublicDeliveryClient deliveryClient, IOptions<DeliveryTrackingOptions> options) : PageModel
{
    private static readonly Regex PublicTokenPattern = new("^[A-Za-z0-9_-]{8,200}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly DeliveryTrackingOptions _options = options.Value;
    private readonly TimeZoneInfo _timeZone = ResolveTimeZone(options.Value.TimeZoneId);

    public PublicDelivery? Delivery { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool DeliveryNotFound { get; private set; }
    public bool IsTerminal => Delivery is not null && IsTerminalStatus(Delivery.Status);
    public string StatusLabel => StatusText(Delivery?.Status);
    public string RecipientFirstName => FirstName(Delivery?.RecipientFirstName ?? Delivery?.CustomerFirstName ?? Delivery?.CustomerName);
    public string OrderNumber => Delivery?.DisplayOrderNumber ?? Delivery?.OrderNumber ?? string.Empty;
    public string DestinationLabel => string.Join(" · ", new[] { Delivery?.DestinationDistrict ?? Delivery?.District ?? Delivery?.Neighborhood ?? Delivery?.Destination?.District ?? Delivery?.Destination?.Neighborhood, Delivery?.DestinationCity ?? Delivery?.City ?? Delivery?.Destination?.City }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string EstimateLabel => Delivery?.Estimate ?? Delivery?.DeliveryEstimate ?? FormatEstimateRange();
    public IReadOnlyList<PublicDeliveryEvent> Timeline => Delivery?.Timeline.Count > 0 ? Delivery.Timeline : Delivery?.Events.Count > 0 ? Delivery.Events : BuildTimeline();
    public IReadOnlyList<PublicDeliveryItem> Items => Delivery?.Items.Count > 0 ? Delivery.Items : Delivery?.Products ?? [];
    public string? GoogleReviewUrl => IsTerminal && IsDelivered(Delivery?.Status) && !string.IsNullOrWhiteSpace(_options.GoogleReviewUrl) ? _options.GoogleReviewUrl : null;
    public string CatalogUrl => _options.CatalogUrl;
    public string WhatsAppUrl => _options.WhatsAppUrl;
    public string InstagramUrl => _options.InstagramUrl;
    public string Host => _options.Host;

    public async Task<IActionResult> OnGetAsync(string? publicToken, CancellationToken cancellationToken)
    {
        ApplyPrivacyHeaders();
        if (string.IsNullOrWhiteSpace(publicToken) || !PublicTokenPattern.IsMatch(publicToken))
        {
            DeliveryNotFound = true;
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }
        try
        {
            Delivery = await deliveryClient.GetAsync(publicToken, cancellationToken);
            DeliveryNotFound = Delivery is null;
            if (DeliveryNotFound) Response.StatusCode = StatusCodes.Status404NotFound;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "Não foi possível consultar a entrega agora.";
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
        catch (HttpRequestException) { ErrorMessage = "Não foi possível consultar a entrega agora."; Response.StatusCode = StatusCodes.Status503ServiceUnavailable; }
        catch (System.Text.Json.JsonException) { ErrorMessage = "Não foi possível consultar a entrega agora."; Response.StatusCode = StatusCodes.Status503ServiceUnavailable; }
        return Page();
    }

    public string EventDate(DateTimeOffset? occurredAt) => occurredAt is { } value ? TimeZoneInfo.ConvertTime(value, _timeZone).ToString("d 'de' MMMM 'às' HH:mm", CultureInfo.GetCultureInfo("pt-BR")) : string.Empty;
    public static bool IsTerminalStatus(string? status) => Normalize(status) is "delivered" or "cancelled" or "canceled" or "returned" or "failed";
    public static bool IsDelivered(string? status) => Normalize(status) == "delivered";

    private string FormatEstimateRange()
    {
        var from = Delivery?.EstimatedDeliveryFrom ?? Delivery?.EstimateFrom ?? Delivery?.EstimatedDeliveryAt ?? Delivery?.ExpectedDeliveryAt ?? Delivery?.EstimatedDate;
        var to = Delivery?.EstimatedDeliveryTo ?? Delivery?.EstimateTo;
        if (from is null) return "A confirmar pela Morita";
        var localFrom = TimeZoneInfo.ConvertTime(from.Value, _timeZone);
        if (to is null) return localFrom.ToString("dddd, d 'de' MMMM", CultureInfo.GetCultureInfo("pt-BR"));
        var localTo = TimeZoneInfo.ConvertTime(to.Value, _timeZone);
        var day = localFrom.Date == localTo.Date ? localFrom.ToString("dddd, d 'de' MMMM", CultureInfo.GetCultureInfo("pt-BR")) : $"{localFrom:d MMM} a {localTo:d MMM}";
        return $"{day}, das {localFrom:HH\\hmm} às {localTo:HH\\hmm}".Replace("h00", "h", StringComparison.Ordinal);
    }

    private IReadOnlyList<PublicDeliveryEvent> BuildTimeline()
    {
        if (Delivery is null) return [];
        var events = new List<PublicDeliveryEvent>();
        AddEvent(events, "Pedido recebido", Delivery.CreatedAt);
        AddEvent(events, "A caminho", Delivery.OutForDeliveryAt);
        AddEvent(events, "Entrega concluída", Delivery.DeliveredAt);
        AddEvent(events, "Entrega cancelada", Delivery.CancelledAt ?? Delivery.CanceledAt);
        if (Delivery.StatusUpdatedAt is { } statusUpdatedAt && !events.Any(item => item.OccurredAt == statusUpdatedAt))
            events.Add(new PublicDeliveryEvent { Title = StatusLabel, OccurredAt = statusUpdatedAt });
        if (events.Count > 0) events[^1].IsCurrent = true;
        return events;
    }

    private static void AddEvent(ICollection<PublicDeliveryEvent> events, string title, DateTimeOffset? occurredAt)
    {
        if (occurredAt is not null) events.Add(new PublicDeliveryEvent { Title = title, OccurredAt = occurredAt });
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        var timeZoneId = string.IsNullOrWhiteSpace(id) ? DeliveryTrackingOptions.DefaultTimeZoneId : id;
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException exception) { throw new InvalidOperationException($"Configured delivery time zone '{timeZoneId}' is not installed.", exception); }
        catch (InvalidTimeZoneException exception) { throw new InvalidOperationException($"Configured delivery time zone '{timeZoneId}' is invalid.", exception); }
    }

    private static string StatusText(string? status) => Normalize(status) switch
    {
        "pending" or "created" => "Pedido recebido",
        "confirmed" => "Entrega confirmada",
        "preparing" or "ready" => "Preparando sua entrega",
        "out_for_delivery" or "in_transit" or "delivering" or "dispatched" => "A caminho",
        "delivered" => "Entrega concluída",
        "cancelled" or "canceled" => "Entrega cancelada",
        "returned" => "Entrega devolvida",
        "failed" => "Entrega não concluída",
        _ => "Acompanhamento da entrega"
    };
    private static string FirstName(string? name) => string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    private static string Normalize(string? status) => (status ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
    {
        "outfordelivery" => "out_for_delivery",
        "intransit" => "in_transit",
        "awaitingconfirmation" => "awaiting_confirmation",
        var normalized => normalized
    };
    private void ApplyPrivacyHeaders()
    {
        Response.Headers.CacheControl = "private, no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
    }
}
