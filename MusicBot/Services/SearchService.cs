using System.Collections.Immutable;
using System.Diagnostics;
using MusicBot.Parsers;
using MusicBot.Utilities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MusicBot.Services;

// Provides methods to get information from search terms.
public class SearchService(ILogger<SearchService> logger)
{
    private const string MetadataFlags =
        "--skip-download --dump-json --no-check-certificate --geo-bypass --ignore-errors --flat-playlist " +
        "--format bestaudio";

    private const string StreamFlags =
        "-f --no-check-certificate --geo-bypass --ignore-errors " +
        "--format bestaudio -o - ";

    private class ThumbnailComparer : Comparer<Thumbnail>
    {
        public override int Compare(Thumbnail? x, Thumbnail? y)
        {
            if (x!.Preference is not null) return x!.Preference.Value.CompareTo(y!.Preference);
            if (x!.Width is not null && x!.Height is not null) return (x!.Width.Value * x!.Height.Value).CompareTo(y!.Width * y!.Height);

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
                .Select(song => {
                    // We don't care about the resolution
                    var thumbnails = song.Thumbnails?
                        .Where(thumb => thumb.Width is not null && thumb.Height is not null)
                        .OrderDescending(new ThumbnailComparer())
                        .Select(thumb => new YoutubeExplode.Common.Thumbnail(thumb.Url, new()))
                        .ToImmutableList();
                    var artists = song.Artists is not null && song.Artists!.Count > 0
                        ? string.Join(", ", song.Artists!)
                        : null;
                    return new CustomSong(song.WebpageUrl, song.Title, artists, thumbnails, GetSongStream(song.WebpageUrl));
                })
                .ToImmutableArray();

            return parsedVideos;
        }
        catch (ArgumentNullException ex)
        {
            logger.LogError(ex, "yt-dlp JSON response was invalid.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting song/playlist metadata for URL '{Url}'.", url);
        }
        
        return [];
    }

    internal Stream GetSongStream(string url)
    {
        var argument = $"{url} {StreamFlags}";

        return GetDlpOutputStream(argument);
    }

    internal async Task<Stream> GetStreamFromUri(Uri url)
    {
        using var client = new HttpClient();
        var responseStream = await client.GetStreamAsync(url);
        return responseStream;
    }
    
    private Process ConfigureDlpProcess(string arguments)
    {
        logger.LogDebug("Starting yt-dlp {Arguments}", arguments);
        var dlpProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };
        return dlpProcess;
    }

    private async Task<string[]> GetDlpOutputStringAsync(string arguments)
    {
        using var dlpProcess = ConfigureDlpProcess(arguments);
        
        dlpProcess.StartInfo.RedirectStandardOutput = true;
        dlpProcess.StartInfo.RedirectStandardError = true;

        dlpProcess.Start();
        
        var outputTask = dlpProcess.StandardOutput.ReadToEndAsync();
        var errorTask = dlpProcess.StandardError.ReadToEndAsync();
        await Task.WhenAll(outputTask, errorTask, dlpProcess.WaitForExitAsync());

        // Process error output information
        var errorOutput = await errorTask;
        if (!string.IsNullOrWhiteSpace(errorOutput))
        {
            logger.LogWarning("yt-dlp message: {ErrorOutput}", errorOutput);
        }

        if (dlpProcess.ExitCode != 0)
        {
            logger.LogError("yt-dlp returned error code {ErrorCode}.", dlpProcess.ExitCode);
        }
        
        // Convert string to array
        var response = await outputTask;
        return response.Split([Environment.NewLine], StringSplitOptions.None);
    }

    private Stream GetDlpOutputStream(string arguments)
    {
        var dlpProcess = ConfigureDlpProcess(arguments);
        dlpProcess.Start();
        
        return dlpProcess.StandardOutput.BaseStream;
    }
}