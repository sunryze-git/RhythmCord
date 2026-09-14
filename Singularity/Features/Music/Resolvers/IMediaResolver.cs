using Singularity.Features.Music.Models;

namespace Singularity.Features.Music.Resolvers;

internal interface IMediaResolver
{
    string Name { get; }
    Task<bool> CanResolveAsync(string query);
    Task<IReadOnlyList<MusicTrackNew>> ResolveAsync(string query);
    Task<Stream?> GetStreamAsync(MusicTrackNew track);
    void PreFetchStreamInfo(MusicTrackNew track);
}
