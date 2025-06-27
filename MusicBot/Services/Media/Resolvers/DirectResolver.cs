using MusicBot.Services.Media.Backends;
using MusicBot.Utilities;
using YoutubeExplode.Videos;

namespace MusicBot.Services.Media.Resolvers;

public class DirectFileResolver(DlpBackend dlpBackend) : IMediaResolver
{
    private static readonly HashSet<string> AudioFileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".ogg", ".opus", ".m4a", ".aac"
    };
    
    public string Name => "Direct";
    public int Priority => 0; // highest priority for direct file resolution
    
    public Task<bool> CanResolveAsync(string query)
    {
        if (!Uri.IsWellFormedUriString(query, UriKind.Absolute))
            return Task.FromResult(false);

        try
        {
            var uri = new Uri(query);
            var ext = Path.GetExtension(uri.LocalPath);
            return Task.FromResult(AudioFileTypes.Contains(ext));
        }
        catch
        {
            return Task.FromResult(false);
        }    }

    public async Task<IReadOnlyList<IVideo>> ResolveAsync(string query)
    {
        var uri = new Uri(query);
        var name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        var stream = await dlpBackend.GetStreamFromUriAsync(uri);
        
        return new List<IVideo> 
        { 
            new CustomSong(query, name, null, null, stream) 
        };
    }

    public Task<Stream> GetStreamAsync(IVideo video)
    {
        if (video is not CustomSong customSong)
            throw new NotSupportedException("DirectFileResolver only supports CustomSong videos");
        
        if (customSong.Source.CanSeek)
            customSong.Source.Position = 0;
        return Task.FromResult(customSong.Source);
    }
}