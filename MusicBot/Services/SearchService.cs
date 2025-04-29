using System.Collections.Immutable;
using System.Diagnostics;
using MusicBot.Parsers;
using MusicBot.Utilities;
using Newtonsoft.Json;

namespace MusicBot.Services;

// Provides methods to get information from search terms.
public static class SearchService
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
    
    internal static async Task<ImmutableArray<CustomSong>> GetMetadataAsync(string url)
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
                    return new CustomSong(song.WebpageUrl, song.Title, thumbnails, GetSongStream(song.Url));
                })
                .ToImmutableArray();

            return parsedVideos;
        }
        catch (ArgumentNullException ex)
        {
            Console.WriteLine($"yt-dlp JSON response was invalid. message={ex.Message} {ex.StackTrace} {ex.Source}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting song/playlist metadata for URL '{url}': {ex.Message}");
        }
        
        return [];
    }

    internal static Stream GetSongStream(string url)
    {
        var argument = $"{url} {StreamFlags}";

        return GetDlpOutputStream(argument);
    }

    internal static async Task<Stream> GetStreamFromUri(Uri url)
    {
        using var client = new HttpClient();
        var responseStream = await client.GetStreamAsync(url);
        return responseStream;
    }
    
    private static Process ConfigureDlpProcess(string arguments)
    {
        Console.WriteLine($"Starting yt-dlp {arguments}");
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

    private static async Task<string[]> GetDlpOutputStringAsync(string arguments)
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
            Console.WriteLine($"yt-dlp error output: {errorOutput}");
        }

        if (dlpProcess.ExitCode != 0)
        {
            Console.WriteLine("yt-dlp failed");
        }
        
        // Convert string to array
        var response = await outputTask;
        return response.Split([Environment.NewLine], StringSplitOptions.None);
    }

    private static Stream GetDlpOutputStream(string arguments)
    {
        var dlpProcess = ConfigureDlpProcess(arguments);
        dlpProcess.Start();
        
        return dlpProcess.StandardOutput.BaseStream;
    }
}