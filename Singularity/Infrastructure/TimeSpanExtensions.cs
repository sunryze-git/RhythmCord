namespace Singularity.Infrastructure;

public static class TimeSpanExtensions
{
    /// <summary>Formats as M:SS, or H:MM:SS from one hour up.</summary>
    public static string ToClockString(this TimeSpan timeSpan) =>
        timeSpan.TotalHours < 1
            ? $"{(int)timeSpan.TotalMinutes}:{timeSpan.Seconds:00}"
            : $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00}";
}
