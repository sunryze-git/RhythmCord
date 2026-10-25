using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Singularity.Features.Music.Models;
using Singularity.Observability;

namespace Singularity.Features.Music.Resolvers;

internal class YtdlpResolver(ILogger<YtdlpResolver> logger, MusicOptions options, BotMetrics metrics) : IMediaResolver
{
    public string Name => "ytdlp";
    public SongSource Source => SongSource.Ytdlp;

    // yt-dlp can stream anything it can open, so it is the universal fallback.
    public bool CanStream(MusicTrackNew track) => QueryUtil.TryGetWebUri(track.Url, out _);

    public Task<bool> CanResolveAsync(string query) => Task.FromResult(!string.IsNullOrWhiteSpace(query));

    /// <summary>
    ///     Looks the query up. Returns an empty list only when yt-dlp ran fine and found nothing; if it could not run,
    ///     timed out or reported an error, that is thrown as <see cref="ResolverFailedException" /> with yt-dlp's reason.
    /// </summary>
    public async Task<IReadOnlyList<MusicTrackNew>> ResolveAsync(string query)
    {
        logger.LogDebug("Resolving query via yt-dlp: {Query}", query);

        query = query.Trim();

        var isWebUrl = QueryUtil.TryGetWebUri(query, out var webUri);
        if (isWebUrl && !await QueryUtil.IsPublicHostAsync(webUri))
            throw new SearchException("That URL is not allowed.");

        return await DumpAsync(isWebUrl ? query : $"ytsearch1:{query}", query);
    }

    public Task<IReadOnlyList<MusicTrackNew>> SearchAsync(string query, int limit)
    {
        query = query.Trim();
        return DumpAsync($"ytsearch{limit}:{query}", query);
    }

    private async Task<IReadOnlyList<MusicTrackNew>> DumpAsync(string target, string query)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // ArgumentList (not a joined string) + "--" so user input can never be parsed as options.
        foreach (var arg in new[]
                 {
                     "--dump-single-json", "--no-warnings", "--flat-playlist",
                     "--playlist-end", options.MaxPlaylistTracks.ToString(), "--", target
                 })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
                            ?? throw new ResolverFailedException("yt-dlp could not be started");
        using var active = metrics.TrackYtdlp();
        using var cts = new CancellationTokenSource(options.YtdlpTimeout);

        string stdout;
        string stderr;
        try
        {
            // Read both pipes concurrently so a full stderr pipe can never block yt-dlp.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            stdout = await stdoutTask;
            stderr = await stderrTask;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new ResolverFailedException("yt-dlp took too long to answer");
        }

        if (process.ExitCode != 0)
        {
            var reason = StderrTail.Describe(stderr.Split('\n'), process.ExitCode);
            logger.LogDebug("yt-dlp exited with code {Code}: {Error}", process.ExitCode, stderr);
            throw new ResolverFailedException(reason);
        }

        try
        {
            return ParseYtdlpJson(stdout, query);
        }
        catch (JsonException ex)
        {
            // Exit code 0 but nothing usable on stdout: a broken or incompatible yt-dlp, not "no results".
            throw new ResolverFailedException("yt-dlp gave an answer I couldn't read", ex);
        }
    }

    public async Task<Stream?> GetStreamAsync(MusicTrackNew track)
    {
        if (!QueryUtil.TryGetWebUri(track.Url, out var uri) || !await QueryUtil.IsPublicHostAsync(uri))
        {
            logger.LogWarning("Refusing to stream non-public or non-http(s) URL for {Title}: {Url}", track.Title, track.Url);
            return null;
        }

        logger.LogDebug("Piping audio stream via yt-dlp for: {Title}", track.Title);

        var psi = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Select best audio stream and pipe output directly to stdout (-)
        foreach (var arg in new[] { "-f", "bestaudio/best", "--no-playlist", "--no-warnings", "-o", "-", "--", uri.AbsoluteUri })
            psi.ArgumentList.Add(arg);

        var process = Process.Start(psi) ?? throw new ResolverFailedException("yt-dlp could not be started");
        var active = metrics.TrackYtdlp();

        var tail = new StderrTail();
        process.ErrorDataReceived += (_, e) =>
        {
            tail.Add(e.Data);
            if (!string.IsNullOrEmpty(e.Data)) logger.LogDebug("yt-dlp: {Line}", e.Data);
        };
        process.BeginErrorReadLine();

        // Return stdout stream directly to FFmpeg/OpusEncoder pipeline
        return new ProcessOutputStream(process, tail, active);
    }

    public void PreFetch(MusicTrackNew track)
    {
        // Nothing to prepare: yt-dlp streams straight to stdout when asked.
    }

    internal static IReadOnlyList<MusicTrackNew> ParseYtdlpJson(string json, string fallbackQuery)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tracks = new List<MusicTrackNew>();

        // Handle playlist or search results collection
        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                var track = MapElementToTrack(entry, fallbackQuery);
                if (track != null) tracks.Add(track);
            }
        }
        else
        {
            // Handle single video payload
            var track = MapElementToTrack(root, fallbackQuery);
            if (track != null) tracks.Add(track);
        }

        return tracks;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    private static MusicTrackNew? MapElementToTrack(JsonElement element, string fallbackQuery)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        var title = GetString(element, "title");
        if (string.IsNullOrEmpty(title)) return null;

        var url = GetString(element, "webpage_url") ?? GetString(element, "url") ?? fallbackQuery;
        var uploader = GetString(element, "uploader") ?? GetString(element, "channel") ?? "Unknown Artist";

        TimeSpan? duration = element.TryGetProperty("duration", out var durProp) &&
                             durProp.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromSeconds(durProp.GetDouble())
            : null;

        return new MusicTrackNew
        {
            Query = fallbackQuery,
            Url = url,
            Title = title,
            Author = uploader,
            Duration = duration,
            ThumbnailUrl = GetString(element, "thumbnail"),
            Source = SongSource.Ytdlp,
            MediaId = GetString(element, "id")
        };
    }
}
