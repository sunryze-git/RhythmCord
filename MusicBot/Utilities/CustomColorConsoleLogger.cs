using Microsoft.Extensions.Logging;

namespace MusicBot.Utilities;

public class FullLineColorConsoleLogger : ILogger
{
    public IDisposable BeginScope<TState>(TState state) => null!;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var color = logLevel switch
        {
            LogLevel.Information => ConsoleColor.Cyan,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Debug => ConsoleColor.Gray,
            _ => ConsoleColor.White
        };

        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;

        var timestamp = DateTime.UtcNow.ToString("MM/dd/yyyy HH:mm:ss.fffffff");
        var scope = ""; // You can enhance this to capture scope if needed

        var message = formatter(state, exception);
        Console.WriteLine($"[{timestamp}] [{logLevel}] {scope}{message}");

        Console.ForegroundColor = originalColor;
    }
}

public class FullLineColorConsoleLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FullLineColorConsoleLogger();
    public void Dispose() { }
}
