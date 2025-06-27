using YoutubeExplode.Videos;

namespace MusicBot.Services.Media.Resolvers;

public interface IMediaResolver
{
    string Name { get; }
    int Priority { get; }
    Task<bool> CanResolveAsync(string query);
    Task<IReadOnlyList<IVideo>> ResolveAsync(string query);
    Task<Stream> GetStreamAsync(IVideo video);
}