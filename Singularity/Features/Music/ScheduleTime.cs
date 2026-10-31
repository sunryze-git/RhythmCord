using System.Globalization;
using System.Text.RegularExpressions;

namespace Singularity.Features.Music;

/// <summary>When a /schedule play starts: a delay ("30m", "1h30m") or a clock time ("21:00", "9pm", "9:30 pm").</summary>
internal static partial class ScheduleTime
{
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromHours(24);

    private static readonly string[] ClockFormats = ["H:mm", "h:mmtt", "h:mm tt", "htt", "h tt"];

    [GeneratedRegex(@"^(?:(?<hours>\d{1,3})\s*h)?\s*(?:(?<minutes>\d{1,4})\s*m)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Delay();

    /// <summary>The moment the text names, or null if it is not a time. A clock time that has passed today means tomorrow.</summary>
    internal static DateTimeOffset? Parse(string text, DateTimeOffset now, TimeZoneInfo zone)
    {
        text = text.Trim();
        return ParseDelay(text) is { } delay ? now + delay : ParseClock(text, now, zone);
    }

    private static TimeSpan? ParseDelay(string text)
    {
        var match = Delay().Match(text);
        var hours = match.Groups["hours"];
        var minutes = match.Groups["minutes"];
        if (!match.Success || (!hours.Success && !minutes.Success)) return null;

        return TimeSpan.FromHours(hours.Success ? int.Parse(hours.Value) : 0)
               + TimeSpan.FromMinutes(minutes.Success ? int.Parse(minutes.Value) : 0);
    }

    private static DateTimeOffset? ParseClock(string text, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!TimeOnly.TryParseExact(text, ClockFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
            return null;

        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var at = InZone(today + clock.ToTimeSpan(), zone);
        return at > now ? at : InZone(today.AddDays(1) + clock.ToTimeSpan(), zone);
    }

    private static DateTimeOffset InZone(DateTime wallClock, TimeZoneInfo zone) => new(wallClock, zone.GetUtcOffset(wallClock));
}
