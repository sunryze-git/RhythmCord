using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using MusicBot.Exceptions;
using MusicBot.Records;
using MusicBot.Utilities;
using Newtonsoft.Json;
using YoutubeExplode.Common;
using Thumbnail = MusicBot.Records.Thumbnail;

namespace MusicBot.Services.Media.Backends;

// Provides methods to get information from search terms.
public class DlpBackend(ILogger<DlpBackend> logger)
{
    private const string MetadataFlags =
        " --skip-download -f bestaudio --dump-json --no-check-certificate --geo-bypass --ignore-errors --flat-playlist ";

    private const string StreamFlags = 
        "-f bestaudio --concurrent-fragments 12 --no-check-certificate --geo-bypass --ignore-errors -o - ";

    private class ThumbnailComparer : Comparer<Thumbnail>
    {
        public override int Compare(Thumbnail? x, Thumbnail? y)
        {
            if (x!.Preference is not null) return x.Preference.Value.CompareTo(y!.Preference);
            if (x.Width is not null && x.Height is not null) return (x.Width.Value * x.Height.Value).CompareTo(y!.Width * y.Height);

            return 0;
        }
    }
    
    internal async Task<ImmutableArray<CustomSong>> GetMetadataAsync(string url)
    {
        var argument = $"{url} {MetadataFlags}";

        try
        {
            // Returns a list of all videos found by the searcher.
            // This allows for playlists to be supported.
            var parseResult = await GetDlpOutputStringAsync(argument);
            var parsedVideos = parseResult
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(JsonConvert.DeserializeObject<Song>)
                .OfType<Song>()
                .Select(song =>
                {
                    var thumbnails = song.Thumbnails?
                        .Where(thumb => thumb.Width is not null && thumb.Height is not null)
                        .OrderDescending(new ThumbnailComparer())
                        .Select(thumb => new YoutubeExplode.Common.Thumbnail(thumb.Url, new Resolution()))
                        .ToImmutableList() ?? ImmutableList<YoutubeExplode.Common.Thumbnail>.Empty;
                    var artists = song.Artists is not null && song.Artists.Count > 0
                        ? string.Join(", ", song.Artists)
                        : song.Uploader;
                    var duration = song.Duration is not null
                        ? TimeSpan.FromSeconds(song.Duration.Value)
                        : TimeSpan.Zero;
                    var thumbnailUrl = thumbnails.FirstOrDefault()?.Url ?? string.Empty;
                    return new CustomSong(
                        url,
                        song.Url,
                        song.Title,
                        artists,
                        duration,
                        thumbnailUrl,
                        SongSource.YouTube // Use YouTube as fallback, or change to SongSource.Ytdlp if you add it
                    );
                })
                .ToImmutableArray();

            return parsedVideos;
        }
        catch (ArgumentNullException ex)
        {
            logger.LogError(ex, "yt-dlp JSON response was invalid.");
        }
        catch (SearchException)
        {
            throw; // pass this one up
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting song/playlist metadata for URL '{Url}'.", url);
        }
        
        return [];
    }

    private async Task<string[]> GetDlpOutputStringAsync(string arguments, int timeoutMs = 30000)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        process.EnableRaisingEvents = false;
        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(); } catch { /* ignore */ }
                throw new TimeoutException($"yt-dlp timed out after {timeoutMs}ms");
            }
            var output = await outputTask;
            var error = await errorTask;
            if (!string.IsNullOrWhiteSpace(error))
                logger.LogWarning("yt-dlp message: {ErrorOutput}", error);
            if (process.ExitCode != 0)
                throw new SearchException(error);
            return output.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "yt-dlp process failed");
            throw;
        }
    }

    public Stream GetSongStream(string url, int timeoutMs = 60000)
    {
        var arguments = $"{url} {StreamFlags}";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = false
        };
        try
        {
            process.Start();
            // Optionally, you can implement a timeout here as well
            return process.StandardOutput.BaseStream;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "yt-dlp process failed to start for streaming");
            process.Dispose();
            throw;
        }
    }
}