using MusicBot.Infrastructure;

namespace MusicBot.Features.Media.Resolvers;

public class DirectFileResolver(HttpClient httpClient) : IMediaResolver
{
    private static readonly HashSet<string> _audioFileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webm", ".mkv", ".flv", ".vob", ".ogv", ".ogg", ".avi", ".mov", ".qt", ".wmv", ".m4v", ".mp4", // video formats
        ".aac", ".aiff", ".alac", ".flac", ".m4a", ".mp1", ".mp2", ".mp3", ".opus", ".wav", ".wma" // audio formats
    };

    public bool Enabled => true;
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
            return Task.FromResult(_audioFileTypes.Contains(ext));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<IReadOnlyList<MusicTrack>> ResolveAsync(string query)
    {
        var uri = new Uri(query);
        var name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);

        return Task.FromResult<IReadOnlyList<MusicTrack>>(new List<MusicTrack>
        {
            new(query, uri.ToString(), name, string.Empty, TimeSpan.Zero, string.Empty, SongSource.Direct)
        });
    }

    public async Task<Stream> GetStreamAsync(MusicTrack video)
    {
        if (video.Source != SongSource.Direct)
            throw new InvalidOperationException($"Cannot stream non-direct song with DirectFileResolver: {video.Title}");
        var stream = await httpClient.GetStreamAsync(video.Url);
        return stream;
    }

    public async Task<bool> CanGetStreamAsync(MusicTrack video) =>
        await Task.FromResult(video.Source == SongSource.Direct);
}
