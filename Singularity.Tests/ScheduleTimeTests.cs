using Singularity.Features.Music;

namespace Singularity.Tests;

public sealed class ScheduleTimeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("30m", 30)]
    [InlineData("2h", 120)]
    [InlineData("1h30m", 90)]
    [InlineData(" 1H 5M ", 65)]
    [InlineData("90m", 90)]
    public void A_delay_counts_from_now(string text, int minutes)
    {
        Assert.Equal(Noon.AddMinutes(minutes), ScheduleTime.Parse(text, Noon, TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData("21:00", "2026-01-15T21:00:00Z")]
    [InlineData("9pm", "2026-01-15T21:00:00Z")]
    [InlineData("9:30 PM", "2026-01-15T21:30:00Z")]
    [InlineData("9:30pm", "2026-01-15T21:30:00Z")]
    [InlineData("08:00", "2026-01-16T08:00:00Z")]
    [InlineData("12:00", "2026-01-16T12:00:00Z")]
    public void A_clock_time_is_its_next_occurrence(string text, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected), ScheduleTime.Parse(text, Noon, TimeZoneInfo.Utc));
    }

    [Fact]
    public void A_clock_time_is_read_in_the_given_time_zone()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        Assert.Equal(DateTimeOffset.Parse("2026-01-16T02:00:00Z"), ScheduleTime.Parse("21:00", Noon, newYork));
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("30")]
    [InlineData("1h30")]
    [InlineData("25:00")]
    [InlineData("-5m")]
    public void Anything_else_is_not_a_time(string text)
    {
        Assert.Null(ScheduleTime.Parse(text, Noon, TimeZoneInfo.Utc));
    }
}
