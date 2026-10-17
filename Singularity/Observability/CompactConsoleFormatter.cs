using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Singularity.Observability;

/// <summary>
///     One short line per entry: <c>23:31:43 INF Playback        message  · guild=… user=…</c>, coloured by level when
///     writing to a terminal (respects NO_COLOR). Exceptions follow, indented.
/// </summary>
internal sealed class CompactConsoleFormatter : ConsoleFormatter
{
    internal const string FormatterName = "compact";
    private const int CategoryWidth = 15;

    private const string Reset = "\e[0m";
    private const string Dim = "\e[90m";

    private readonly bool _color =
        !Console.IsOutputRedirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

    private readonly IOptionsMonitor<ConsoleFormatterOptions> _options;

    public CompactConsoleFormatter(IOptionsMonitor<ConsoleFormatterOptions> options) : base(FormatterName) =>
        _options = options;

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (message is null && logEntry.Exception is null) return;

        var sb = new StringBuilder(160);
        var (label, color) = logEntry.LogLevel switch
        {
            LogLevel.Trace => ("TRC", Dim),
            LogLevel.Debug => ("DBG", Dim),
            LogLevel.Information => ("INF", "\e[32m"),
            LogLevel.Warning => ("WRN", "\e[33m"),
            LogLevel.Error => ("ERR", "\e[31m"),
            LogLevel.Critical => ("CRT", "\e[1;41;97m"),
            _ => ("???", Dim)
        };

        Paint(sb, Dim, DateTime.Now.ToString("HH:mm:ss"));
        sb.Append(' ');
        Paint(sb, color, label);
        sb.Append(' ');
        Paint(sb, "\e[36m", ShortCategory(logEntry.Category).PadRight(CategoryWidth));
        sb.Append(' ');
        sb.Append(message);

        if (scopeProvider is not null && _options.CurrentValue.IncludeScopes)
        {
            var scopes = new StringBuilder();
            scopeProvider.ForEachScope(static (scope, s) =>
            {
                if (s.Length > 0) s.Append(' ');
                s.Append(scope);
            }, scopes);

            if (scopes.Length > 0)
            {
                sb.Append("  ");
                Paint(sb, Dim, "· " + scopes);
            }
        }

        if (logEntry.Exception is { } ex)
        {
            sb.AppendLine();
            foreach (var line in ex.ToString().Split('\n'))
            {
                sb.Append("    ");
                Paint(sb, Dim, line.TrimEnd('\r'));
                sb.AppendLine();
            }

            sb.Length -= Environment.NewLine.Length;
        }

        textWriter.WriteLine(sb.ToString());
    }

    private void Paint(StringBuilder sb, string color, string text)
    {
        if (_color) sb.Append(color).Append(text).Append(Reset);
        else sb.Append(text);
    }

    // "Singularity.Features.Music.Services.PlaybackHandler" -> "PlaybackHandler"
    internal static string ShortCategory(string category)
    {
        var dot = category.LastIndexOf('.');
        var name = dot >= 0 ? category[(dot + 1)..] : category;
        return name.Length <= CategoryWidth ? name : name[..(CategoryWidth - 1)] + "…";
    }
}
