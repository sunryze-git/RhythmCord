using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace MusicBot.Services;

// Provides methods to get information from search terms.
public static class SearchService
{
    private static readonly Regex UrlRegex = new("@\"^(https?|ftp)://[^\\s/$.?#].[^\\s]*$\"");

    private const string MetadataFlags =
        "--skip-download --dump-json --no-check-certificate --geo-bypass --ignore-errors --flat-playlist " +
        "--format bestaudio";

    private const string StreamFlags =
        "-f --no-check-certificate --geo-bypass --ignore-errors " +
        "--format bestaudio -o - ";
    
    internal static async Task<ImmutableArray<Parsers.Song>> GetYouTubeMetadataAsync(string term)
    {
        var argument = UrlRegex.IsMatch(term)
            ? $"{term} {MetadataFlags}"
            : $"ytsearch:\"{term}\" {MetadataFlags}";

        try
        {
            // Returns a list of all videos found by the searcher.
            // This allows for playlists to be supported.
            var parseResult = await GetDlpOutputStringAsync(argument);
            var parsedVideos = parseResult
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(JsonConvert.DeserializeObject<Parsers.Song>)
                .OfType<Parsers.Song>()
                .ToImmutableArray();

            return parsedVideos;
        }
        catch (ArgumentNullException)
        {
            Console.WriteLine($"YT-DLP JSON response was invalid.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting YouTube metadata for term '{term}': {ex.Message}");
        }
        
        return ImmutableArray<Parsers.Song>.Empty;
    }

    internal static Stream GetYouTubeStream(string term)
    {
        var argument = UrlRegex.IsMatch(term)
            ? $"{term} {StreamFlags} --flat-playlist"
            : $"ytsearch:\"{term}\" {StreamFlags}";

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
        var dlpProcess = new Process();
        dlpProcess.StartInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
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
            Console.WriteLine($"YT-DLP: {errorOutput}");
        }

        if (dlpProcess.ExitCode != 0)
        {
            Console.WriteLine("YT-DLP Failed");
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