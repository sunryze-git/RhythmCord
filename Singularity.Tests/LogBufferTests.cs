using Singularity.Observability;

namespace Singularity.Tests;

public class LogBufferTests
{
    [Theory]
    [InlineData("guild 123456789012345678 left", "guild [id] left")]
    [InlineData("user 987654321098765432 and 111111111111111111", "user [id] and [id]")]
    [InlineData("<@123456789012345678> pinged", "<@[id]> pinged")]
    [InlineData("ports 8787 and 443 are fine", "ports 8787 and 443 are fine")]
    [InlineData("a 1234567890123456 (16 digits) stays", "a 1234567890123456 (16 digits) stays")]
    public void Snowflakes_are_redacted(string input, string expected) =>
        Assert.Equal(expected, LogBuffer.Redact(input));

    [Fact]
    public void Buffer_keeps_only_the_newest_entries()
    {
        var buffer = new LogBuffer(capacity: 3);
        for (var i = 0; i < 5; i++) buffer.Add(new LogLine(DateTimeOffset.UtcNow, "info", "c", $"m{i}"));

        Assert.Equal(["m2", "m3", "m4"], buffer.Snapshot(10).Select(l => l.Message));
        Assert.Equal(["m3", "m4"], buffer.Snapshot(2).Select(l => l.Message));
    }
}
