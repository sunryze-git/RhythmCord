using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Singularity.Observability;

namespace Singularity.Tests;

public class CompactConsoleFormatterTests
{
    private static string Render(LogLevel level, string category, string message, Exception? ex = null, string? scope = null, bool scopes = true)
    {
        var formatter = new CompactConsoleFormatter(new StaticMonitor<ConsoleFormatterOptions>(new ConsoleFormatterOptions { IncludeScopes = scopes }));
        var provider = new LoggerExternalScopeProvider();
        using var _ = scope is null ? null : provider.Push(scope);
        var writer = new StringWriter();

        formatter.Write(new LogEntry<string>(level, category, new EventId(0), message, ex, (s, _) => s), provider, writer);
        return writer.ToString();
    }

    [Fact]
    public void Line_has_level_short_category_and_message()
    {
        var line = Render(LogLevel.Warning, "Singularity.Features.Music.Services.MediaResolver", "falling back");

        Assert.Contains("WRN", line);
        Assert.Contains("MediaResolver", line);
        Assert.DoesNotContain("Singularity.Features", line);
        Assert.Contains("falling back", line);
    }

    [Fact]
    public void Scopes_are_appended_when_enabled_and_omitted_when_not()
    {
        Assert.Contains("guild=1 user=2", Render(LogLevel.Information, "X", "hi", scope: "guild=1 user=2"));
        Assert.DoesNotContain("guild=1", Render(LogLevel.Information, "X", "hi", scope: "guild=1 user=2", scopes: false));
    }

    [Fact]
    public void Exceptions_follow_on_indented_lines()
    {
        var text = Render(LogLevel.Error, "X", "broke", new InvalidOperationException("boom"));

        Assert.Contains("ERR", text);
        Assert.Contains("    System.InvalidOperationException: boom", text);
    }

    [Fact]
    public void Redirected_output_has_no_ansi_escapes() =>
        Assert.DoesNotContain("\e[", Render(LogLevel.Error, "X", "plain"));

    [Theory]
    [InlineData("A.B.C", "C")]
    [InlineData("Plain", "Plain")]
    [InlineData("A.AVeryLongCategoryNameIndeed", "AVeryLongCateg…")]
    public void ShortCategory_trims_namespace_and_length(string input, string expected) =>
        Assert.Equal(expected, CompactConsoleFormatter.ShortCategory(input));
}
