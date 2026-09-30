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

    // Opening hours: change these values and every place on the site follows
    // (footer, "Aberto agora" badge, FAQ answer and the schema.org openingHours).
    private const int WeekdayOpenHour = 10;
    private const int WeekdayCloseHour = 18;
    private const int SaturdayOpenHour = 10;
    private const int SaturdayCloseHour = 14;

    public static readonly IReadOnlyList<OpeningHours> Hours =
    [
        new(DayOfWeek.Monday, WeekdayOpenHour, WeekdayCloseHour),
        new(DayOfWeek.Tuesday, WeekdayOpenHour, WeekdayCloseHour),
        new(DayOfWeek.Wednesday, WeekdayOpenHour, WeekdayCloseHour),
        new(DayOfWeek.Thursday, WeekdayOpenHour, WeekdayCloseHour),
        new(DayOfWeek.Friday, WeekdayOpenHour, WeekdayCloseHour),
        new(DayOfWeek.Saturday, SaturdayOpenHour, SaturdayCloseHour),
    ];

    public static readonly IReadOnlyList<string> HoursSummary =
    [
        $"Segunda a sexta: {WeekdayOpenHour}h às {WeekdayCloseHour}h",
        $"Sábado: {SaturdayOpenHour}h às {SaturdayCloseHour}h",
        "Domingo: fechado",
    ];

    public static string HoursSentence =>
        $"de segunda a sexta, das {WeekdayOpenHour}h às {WeekdayCloseHour}h, e aos sábados, das {SaturdayOpenHour}h às {SaturdayCloseHour}h";

    /// <summary>schema.org openingHours values, e.g. ["Mo-Fr 10:00-18:00", "Sa 10:00-14:00"].</summary>
    public static string OpeningHoursJsonLd => JsonSerializer.Serialize(new[]
    {
        $"Mo-Fr {WeekdayOpenHour:00}:00-{WeekdayCloseHour:00}:00",
        $"Sa {SaturdayOpenHour:00}:00-{SaturdayCloseHour:00}:00",
    });

    /// <summary>JSON consumed by wwwroot/js/store-hours.js: { "1": [9, 18], ... } keyed by JS weekday (0 = Sunday).</summary>
    public static string HoursJson => JsonSerializer.Serialize(
        Hours.ToDictionary(h => ((int)h.Day).ToString(), h => new[] { h.OpenHour, h.CloseHour }));
}

public record OpeningHours(DayOfWeek Day, int OpenHour, int CloseHour);
