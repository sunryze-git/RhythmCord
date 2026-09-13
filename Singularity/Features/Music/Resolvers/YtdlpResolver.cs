using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Singularity.Features.Music.Models;

namespace Singularity.Features.Music.Resolvers;

public class YtdlpResolver(ILogger<YtdlpResolver> logger) : IMediaResolver
{
    public string Name => "YT-DLP";

    public Task<bool> CanResolveAsync(string query) => Task.FromResult(!string.IsNullOrWhiteSpace(query));

    public async Task<IReadOnlyList<MusicTrackNew>> ResolveAsync(string query)
    {
        logger.LogInformation("Resolving query via yt-dlp: {Query}", query);

        // Format search terms if not a direct URL
        var isUrl = Uri.TryCreate(query, UriKind.Absolute, out _);
        var targetQuery = isUrl ? query : $"ytsearch1:{query}";

        var psi = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            Arguments = $"--dump-single-json --no-warnings --flat-playlist \"{targetQuery}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                logger.LogError("Failed to start yt-dlp process.");
                return [];
            }

            var jsonOutput = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                logger.LogError("yt-dlp exited with code {Code}: {Error}", process.ExitCode, error);
                return [];
            }

            return ParseYtdlpJson(jsonOutput, query);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve query with yt-dlp: {Query}", query);
            return [];
        }
    }

    public Task<Stream?> GetStreamAsync(MusicTrackNew track)
    {
        logger.LogInformation("Piping audio stream via yt-dlp for: {Title}", track.Title);

        var psi = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            // Select best audio stream and pipe output directly to stdout (-)
            Arguments = $"-f bestaudio/best -o - \"{track.Url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi);
        if (process is null)
        {
            logger.LogError("Failed to start yt-dlp process for streaming.");
            return Task.FromResult<Stream?>(null);
        }

        // Return stdout stream directly to FFmpeg/OpusEncoder pipeline
        return Task.FromResult<Stream?>(process.StandardOutput.BaseStream);
    }

    public void PreFetchStreamInfo(MusicTrackNew track)
    {
        // No-op for yt-dlp stdout piping
    }

    private static IReadOnlyList<MusicTrackNew> ParseYtdlpJson(string json, string fallbackQuery)
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

    private static MusicTrackNew? MapElementToTrack(JsonElement element, string fallbackQuery)
    {
        var title = element.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
        if (string.IsNullOrEmpty(title)) return null;

        var url = element.TryGetProperty("webpage_url", out var urlProp)
            ? urlProp.GetString()
            : element.TryGetProperty("url", out var u) ? u.GetString() : fallbackQuery;

        var uploader = element.TryGetProperty("uploader", out var uploaderProp)
            ? uploaderProp.GetString()
            : element.TryGetProperty("channel", out var chProp) ? chProp.GetString() : "Unknown Artist";

        double? durationSeconds = element.TryGetProperty("duration", out var durProp) && durProp.ValueKind == JsonValueKind.Number
            ? durProp.GetDouble()
            : null;

        var thumbnail = element.TryGetProperty("thumbnail", out var thumbProp) ? thumbProp.GetString() : null;
        var mediaId = element.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;

        return new MusicTrackNew
        {
            Query = fallbackQuery,
            Url = url ?? fallbackQuery,
            Title = title,
            Author = uploader ?? "Unknown Artist",
            Duration = durationSeconds.HasValue ? TimeSpan.FromSeconds(durationSeconds.Value) : null,
            ThumbnailUrl = thumbnail,
            Source = SongSource.Ytdlp,
            MediaId = mediaId
        };
    }
}
