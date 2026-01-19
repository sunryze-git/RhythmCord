namespace MusicBot.Infrastructure;

public static class TimeSpanExtensions
{
    /// <summary>
    ///     Formats a TimeSpan dynamically based on its total hours.
    ///     If less than an hour, shows MM:SS.FF.
    ///     If an hour or more, shows H:MM:SS.FF.
    /// </summary>
    /// <param name="timeSpan">The TimeSpan to format.</param>
    /// <returns>The formatted string.</returns>
    public static string ToAdaptivePlaybackString(this TimeSpan timeSpan)
    {
        return timeSpan.TotalHours < 1
            ?
            // Less than an hour: MM:SS.FF
            // MM: Minutes (two digits, leading zero)
            // SS: Seconds (two digits, leading zero)
            // FF: Hundredths of a second (two digits, leading zeros)
            timeSpan.ToString(@"mm\:ss\.ff")
            :
            // An hour or more: H:MM:SS.FF
            // H: Hours (no leading zero)
            // MM: Minutes (two digits, leading zero)
            // SS: Seconds (two digits, leading zero)
            // FF: Hundredths of a second (two digits, leading zeros)
            // Note: TotalHours gives you the total hours including days, etc.
            // But if you want just the "Hours" property (0-23) for H,
            // and TotalHours > 1 to mean "show H", you might need to adjust.
            // For simple music playback, TotalHours is usually fine for the "H" part.
            $@"{(int)timeSpan.TotalHours}:{timeSpan:mm\:ss\.ff}";
        // Alternative if you strictly want the 'Hours' property (0-23)
        // and handle days separately, which is less common for simple playback:
        // return $"{timeSpan.Hours}:{timeSpan.ToString(@"mm\:ss\.ff")}";
    }
}
