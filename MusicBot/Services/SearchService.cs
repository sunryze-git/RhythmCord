using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using MusicBot.Parsers;
using MusicBot.Utilities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MusicBot.Services;

// Provides methods to get information from search terms.
public class SearchService
{
    private readonly HttpClient _httpClient;
    private readonly Process _dlpProcess = new();
    private readonly ILogger<SearchService> _logger;

    public SearchService(ILogger<SearchService> logger)
    {
        // Initializing the process here with its start info can save 
        // a very small amount of time, but its a good optimization strategy
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            EnableMultipleHttp3Connections = true,
            MaxConnectionsPerServer = 100,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        _httpClient = new HttpClient(handler);
        _logger = logger;
        _dlpProcess.StartInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            Arguments = string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        _dlpProcess.EnableRaisingEvents = true;
        _dlpProcess.Exited += (sender, args) =>
        {
            _logger.LogWarning("yt-dlp process exited with code {ExitCode}.", _dlpProcess.ExitCode);
            // YT-DLP Log
            var errorOutput = _dlpProcess.StandardError.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(errorOutput))
            {
                _logger.LogWarning("YT-DLP Log: {ErrorOutput}", errorOutput);
            }
        };
    }

    private const string MetadataFlags =
        """ --skip-download -f "ba[acodec=opus]" --dump-json --no-check-certificate --geo-bypass --ignore-errors --flat-playlist """;

    private const string StreamFlags = """-f "ba[acodec=opus]" --concurrent-fragments 12 --no-check-certificate --geo-bypass --ignore-errors -o - """;

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
            _logger.LogError(ex, "yt-dlp JSON response was invalid.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting song/playlist metadata for URL '{Url}'.", url);
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
        return await _httpClient.GetStreamAsync(url);
    }

    private void StartDlpOperation(string arguments)
    {
        _dlpProcess.StartInfo.Arguments = arguments;
        _dlpProcess.Start();
    }

    private async Task<string[]> GetDlpOutputStringAsync(string arguments)
    {
        StartDlpOperation(arguments);

        var outputTask = _dlpProcess.StandardOutput.ReadToEndAsync();
        var errorTask = _dlpProcess.StandardError.ReadToEndAsync();
        await Task.WhenAll(outputTask, errorTask, _dlpProcess.WaitForExitAsync());

        // Process error output information
        var errorOutput = await errorTask;
        if (!string.IsNullOrWhiteSpace(errorOutput))
        {
            _logger.LogWarning("yt-dlp message: {ErrorOutput}", errorOutput);
        }

        if (_dlpProcess.ExitCode != 0)
        {
            _logger.LogError("yt-dlp returned error code {ErrorCode}.", _dlpProcess.ExitCode);
        }
        
        // Convert string to array
        var response = await outputTask;
        return response.Split([Environment.NewLine], StringSplitOptions.None);
    }

    private Stream GetDlpOutputStream(string arguments)
    {
        StartDlpOperation(arguments);
        return _dlpProcess.StandardOutput.BaseStream;
    }
}