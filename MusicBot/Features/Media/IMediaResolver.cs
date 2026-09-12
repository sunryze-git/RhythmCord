using MusicBot.Infrastructure;

namespace MusicBot.Features.Media.Resolvers;

public interface IMediaResolver
{
    bool Enabled { get; }
    string Name { get; }
    int Priority { get; }

    /// <summary>
    ///     Initial check to see if this resolver can handle the query.
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    Task<bool> CanResolveAsync(string query);

    /// <summary>
    ///     Attempt to resolve the query into a list of CustomSong objects.
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    Task<IReadOnlyList<MusicTrack>> ResolveAsync(string query);

    /// <summary>
    ///     Get the Stream for a given CustomSong.
    /// </summary>
    /// <param name="video"></param>
    /// <returns></returns>
    Task<Stream> GetStreamAsync(MusicTrack video);

    /// <summary>
    ///     Tests if a given CustomSong can be resolved to a stream by this resolver.
    /// </summary>
    /// <param name="video"></param>
    /// <returns></returns>
    Task<bool> CanGetStreamAsync(MusicTrack video);
}
