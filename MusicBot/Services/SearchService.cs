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
    private readonly HttpClient _httpClient = new();
    private readonly Process _dlpProcess = new();
    
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

    private Stream GetSongStream(string url)
    {
        var argument = $"{url} {StreamFlags}";

        return GetDlpOutputStream(argument);
    }

    internal async Task<Stream> GetStreamFromUri(Uri url)
    {
        var responseStream = await _httpClient.GetStreamAsync(url);
        return responseStream;
    }

    private void ConfigureDlpProcess(string arguments)
    {
        _dlpProcess.StartInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
    }

    private async Task<string[]> GetDlpOutputStringAsync(string arguments)
    {
        ConfigureDlpProcess(arguments);
        _dlpProcess.Start();
        
        var outputTask = _dlpProcess.StandardOutput.ReadToEndAsync();
        var errorTask = _dlpProcess.StandardError.ReadToEndAsync();
        await Task.WhenAll(outputTask, errorTask, _dlpProcess.WaitForExitAsync());

        // Process error output information
        var errorOutput = await errorTask;
        if (!string.IsNullOrWhiteSpace(errorOutput))
        {
            logger.LogWarning("yt-dlp message: {ErrorOutput}", errorOutput);
        }

        if (_dlpProcess.ExitCode != 0)
        {
            logger.LogError("yt-dlp returned error code {ErrorCode}.", _dlpProcess.ExitCode);
        }
        
        // Convert string to array
        var response = await outputTask;
        return response.Split([Environment.NewLine], StringSplitOptions.None);
    }

    private Stream GetDlpOutputStream(string arguments)
    {
        ConfigureDlpProcess(arguments);
        _dlpProcess.Start();
        return _dlpProcess.StandardOutput.BaseStream;
    }
}