namespace Tafaqqad.Api.Infrastructure;

/// Business dates (booking days) follow Jordan's calendar, not the server clock or UTC.
public static class JordanClock
{
    private static readonly TimeZoneInfo Zone = FindZone();

    public static DateOnly Today =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone));

    private static TimeZoneInfo FindZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Amman");            // IANA name
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Jordan Standard Time");  // Windows name
        }
    }
}