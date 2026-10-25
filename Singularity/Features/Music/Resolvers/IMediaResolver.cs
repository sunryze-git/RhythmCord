using Singularity.Features.Music.Models;

namespace Singularity.Features.Music.Resolvers;

internal interface IMediaResolver
{
    /// <summary>Stable lower-case key used in configuration (Music:ResolverOrder) and statistics.</summary>
    string Name { get; }

    /// <summary>Tracks produced by this resolver carry this source.</summary>
    SongSource Source { get; }

    /// <summary>Whether this resolver is willing to look the query up (a URL it understands, or free text).</summary>
    Task<bool> CanResolveAsync(string query);

    Task<IReadOnlyList<MusicTrackNew>> ResolveAsync(string query);

    /// <summary>Up to <paramref name="limit" /> matches for free text (never a URL), best first.</summary>
    Task<IReadOnlyList<MusicTrackNew>> SearchAsync(string query, int limit);

    /// <summary>Whether this resolver can open an audio stream for the track (not necessarily one it resolved).</summary>
    bool CanStream(MusicTrackNew track);

    Task<Stream?> GetStreamAsync(MusicTrackNew track);

    /// <summary>Starts any slow preparation for a track that is about to play. Must not throw.</summary>
    void PreFetch(MusicTrackNew track);
}
