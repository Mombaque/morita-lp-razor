using System.Text.Json;

namespace Morita.LP.Razor.Services;

public static class StoreInfo
{
    public const string Name = "Morita";
    public const string Street = "Rua Coronel Nogueira Padilha, 429";
    public const string City = "Sorocaba";
    public const string State = "SP";
    public const string PhoneDisplay = "(15) 98107-9332";
    public const string MapsUrl = "https://maps.app.goo.gl/pNYYiMimWGa4actz7";
    public const string InstagramUrl = "https://www.instagram.com/morita.fight/";
    public const string InstagramHandle = "@morita.fight";
    public const string TimeZoneId = "America/Sao_Paulo";

    public static string AddressLine => $"{Street} - {City}, {State}";

    public static readonly IReadOnlyList<OpeningHours> Hours =
    [
        new(DayOfWeek.Monday, 9, 18),
        new(DayOfWeek.Tuesday, 9, 18),
        new(DayOfWeek.Wednesday, 9, 18),
        new(DayOfWeek.Thursday, 9, 18),
        new(DayOfWeek.Friday, 9, 18),
        new(DayOfWeek.Saturday, 9, 13),
    ];

    public static readonly IReadOnlyList<string> HoursSummary =
    [
        "Segunda a sexta: 9h às 18h",
        "Sábado: 9h às 13h",
        "Domingo: fechado",
    ];

    /// <summary>JSON consumed by wwwroot/js/store-hours.js: { "1": [9, 18], ... } keyed by JS weekday (0 = Sunday).</summary>
    public static string HoursJson => JsonSerializer.Serialize(
        Hours.ToDictionary(h => ((int)h.Day).ToString(), h => new[] { h.OpenHour, h.CloseHour }));
}

public record OpeningHours(DayOfWeek Day, int OpenHour, int CloseHour);
