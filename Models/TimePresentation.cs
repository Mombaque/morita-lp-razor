namespace Morita.LP.Razor.Models;

public static class TimePresentation
{
    // Brasília time has no daylight saving since 2019; a fixed offset avoids depending on the host's time zone data.
    private static readonly TimeSpan StoreOffset = TimeSpan.FromHours(-3);

    public static DateTimeOffset ToStoreTime(DateTimeOffset value) => value.ToOffset(StoreOffset);
}
