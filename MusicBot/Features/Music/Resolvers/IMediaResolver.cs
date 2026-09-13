using MusicBot.Features.Music.Models;

namespace MusicBot.Features.Music.Resolvers;

public interface IMediaResolver
{
    string Name { get; }
    Task<bool> CanResolveAsync(string query);
    Task<IReadOnlyList<MusicTrackNew>> ResolveAsync(string query);
    Task<Stream?> GetStreamAsync(MusicTrackNew track);
    void PreFetchStreamInfo(MusicTrackNew track);
}
