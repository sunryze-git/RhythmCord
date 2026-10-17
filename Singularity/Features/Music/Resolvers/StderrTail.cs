using System.Text.RegularExpressions;

namespace Singularity.Features.Music.Resolvers;

/// <summary>Keeps the last few stderr lines of a child process so a failure can be explained afterwards.</summary>
internal sealed partial class StderrTail(int capacity = 8)
{
    private readonly Queue<string> _lines = new();
    private readonly Lock _lock = new();

    [GeneratedRegex(@"\e\[[0-9;]*[A-Za-z]")]
    private static partial Regex Ansi();

    // "[youtube] dQw4w9WgXcQ: " style prefixes carry nothing the user needs.
    [GeneratedRegex(@"^\[[^\]]+\]\s+\S+:\s+")]
    private static partial Regex ExtractorPrefix();

    internal void Add(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        lock (_lock)
        {
            if (_lines.Count >= capacity) _lines.Dequeue();
            _lines.Enqueue(line);
        }
    }

    internal string Reason(int exitCode)
    {
        string[] lines;
        lock (_lock) lines = _lines.ToArray();
        return Describe(lines, exitCode);
    }

    /// <summary>Picks the most useful line (yt-dlp prefixes real failures with "ERROR:") and cleans it for display.</summary>
    internal static string Describe(IReadOnlyList<string> lines, int exitCode)
    {
        var line = lines.LastOrDefault(l => l.Contains("ERROR:", StringComparison.OrdinalIgnoreCase))
                   ?? lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));

        if (line is null) return $"yt-dlp exited with code {exitCode}";

        line = Ansi().Replace(line, "");
        var marker = line.IndexOf("ERROR:", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) line = line[(marker + "ERROR:".Length)..];

        line = ExtractorPrefix().Replace(line.Trim(), "").Trim();
        line = Regex.Replace(line, @"\s+", " ");
        if (line.Length > 200) line = line[..199] + "…";

        return line.Length == 0 ? $"yt-dlp exited with code {exitCode}" : line;
    }
}
