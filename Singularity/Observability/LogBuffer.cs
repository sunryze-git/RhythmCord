using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Singularity.Observability;

public sealed record LogLine(DateTimeOffset Time, string Level, string Category, string Message);

/// <summary>Ring buffer of recent log lines for the dashboard. Messages are redacted before they are stored.</summary>
public sealed partial class LogBuffer(int capacity = 300)
{
    private readonly Queue<LogLine> _lines = new();
    private readonly Lock _lock = new();

    // Discord snowflakes (guild/channel/user/message ids) are 17-20 digits.
    [GeneratedRegex(@"\b\d{17,20}\b")]
    private static partial Regex SnowflakeRegex();

    /// <summary>Strips identifiers so the dashboard never reveals which guild/user/channel an event belongs to.</summary>
    internal static string Redact(string text) => SnowflakeRegex().Replace(text, "[id]");

    internal void Add(LogLine line)
    {
        lock (_lock)
        {
            if (_lines.Count >= capacity) _lines.Dequeue();
            _lines.Enqueue(line);
        }
    }

    internal IReadOnlyList<LogLine> Snapshot(int max)
    {
        lock (_lock) return _lines.TakeLast(max).ToList();
    }
}

/// <summary>Feeds the log ring buffer and the warning/error counters. Scopes (guild/user context) are never stored.</summary>
internal sealed class BufferLoggerProvider(LogBuffer buffer, BotMetrics metrics) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new BufferLogger(CompactConsoleFormatter.ShortCategory(categoryName), buffer, metrics);

    public void Dispose() { }

    private sealed class BufferLogger(string category, LogBuffer buffer, BotMetrics metrics) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            if (logLevel == LogLevel.Warning) metrics.LogEntry("warning");
            else if (logLevel >= LogLevel.Error) metrics.LogEntry("error");

            var message = formatter(state, exception);
            if (exception is not null)
            {
                var firstLine = exception.Message.Split('\n')[0].TrimEnd('\r');
                message += $" — {exception.GetType().Name}: {firstLine}";
            }

            var level = logLevel switch
            {
                LogLevel.Information => "info",
                LogLevel.Warning => "warn",
                _ => "error"
            };

            buffer.Add(new LogLine(DateTimeOffset.UtcNow, level, category, LogBuffer.Redact(message)));
        }
    }
}
