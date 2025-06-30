using MusicBot.Utilities;
using YoutubeExplode.Videos;

namespace MusicBot.Services.Media.Resolvers;

public interface IMediaResolver
{
    string Name { get; }
    int Priority { get; }
    
    /// <summary>
    /// Initial check to see if this resolver can handle the query.
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    Task<bool> CanResolveAsync(string query);
    
    /// <summary>
    /// Attempt to resolve the query into a list of CustomSong objects.
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    Task<IReadOnlyList<CustomSong>> ResolveAsync(string query);
    
    /// <summary>
    /// Get the Stream for a given CustomSong.
    /// </summary>
    /// <param name="video"></param>
    /// <returns></returns>
    Task<Stream> GetStreamAsync(CustomSong video);
    
    /// <summary>
    /// Tests if a given CustomSong can be resolved to a stream by this resolver.
    /// </summary>
    /// <param name="video"></param>
    /// <returns></returns>
    Task<bool> CanGetStreamAsync(CustomSong video);
}