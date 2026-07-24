namespace HstReceipts.Core;

/// <summary>
/// Eastern Time (America/Toronto) — observes EST/EDT. Used for CreatedAtEst / ModifiedAtEst.
/// </summary>
public static class EasternTime
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    public static DateTime Now
    {
        get
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);
            return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        }
    }

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Eastern Standard Time", "America/Toronto", "America/New_York" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        // Fallback: fixed UTC-5 (does not observe DST).
        return TimeZoneInfo.CreateCustomTimeZone(
            "Eastern-Fallback",
            TimeSpan.FromHours(-5),
            "Eastern Time",
            "Eastern Time");
    }
}
