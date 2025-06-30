using MusicBot.Services.Media.Backends;
using MusicBot.Utilities;
using YoutubeExplode.Videos;

namespace MusicBot.Services.Media.Resolvers;

public class DirectFileResolver(DlpBackend dlpBackend, HttpClient httpClient) : IMediaResolver
{
    private static readonly HashSet<string> AudioFileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webm", ".mkv", ".flv", ".vob", ".ogv", ".ogg", ".avi", ".mov", ".qt", ".wmv", ".m4v", ".mp4", // video formats
        ".aac", ".aiff", ".alac", ".flac", ".m4a", ".mp1", ".mp2", ".mp3", ".opus", ".wav", ".wma" // audio formats
    };
    
    public string Name => "Direct";
    public int Priority => 0; // highest priority for direct file resolution
    
    public Task<bool> CanResolveAsync(string query)
    {
        if (!Uri.IsWellFormedUriString(query, UriKind.Absolute))
            return Task.FromResult(false); // not a URL.

        try
        {
            var uri = new Uri(query);
            var ext = Path.GetExtension(uri.LocalPath);
            return Task.FromResult(AudioFileTypes.Contains(ext));
        }
        catch
        {
            return Task.FromResult(false);
        }    
    }

    public async Task<IReadOnlyList<CustomSong>> ResolveAsync(string query)
    {
        var uri = new Uri(query);
        var name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);

        return new List<CustomSong>
        {
            new(query, uri.ToString(), name, string.Empty, TimeSpan.Zero, string.Empty, SongSource.Direct)
        };
    }

    public async Task<Stream> GetStreamAsync(CustomSong video)
    {
        if (video.Source != SongSource.Direct)
            throw new Exception($"Cannot stream non-direct song with DirectFileResolver: {video.Title}");
        var stream = await httpClient.GetStreamAsync(video.Url);
        return stream;
    }

    public async Task<bool> CanGetStreamAsync(CustomSong video)
    {
        return await Task.FromResult(video.Source == SongSource.Direct);

    }
}