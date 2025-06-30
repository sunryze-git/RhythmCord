using MusicBot.Services.Media.Backends;
using MusicBot.Utilities;

namespace MusicBot.Services.Media.Resolvers;

public class YtdlpResolver(DlpBackend dlpService) : IMediaResolver
{
    public string Name => "YT-DLP";
    public int Priority => 99;
    public Task<bool> CanResolveAsync(string query)
    {
        return Task.FromResult(Uri.IsWellFormedUriString(query, UriKind.Absolute));
    }

    public async Task<IReadOnlyList<CustomSong>> ResolveAsync(string query)
    {
        var results = await dlpService.GetMetadataAsync(query);
        // Map IVideo results to CustomSong with SongSource.Ytdlp (if it exists, else use SongSource.YouTube)
        return results.Select(v => new CustomSong(v, query, SongSource.YouTube)).ToList();
    }

    public Task<Stream> GetStreamAsync(CustomSong video)
    {
        // Use DlpBackend to get the stream for the song's URL
        return Task.FromResult(dlpService.GetSongStream(video.Url));
    }

    public Task<bool> CanGetStreamAsync(CustomSong video)
    {
        // Only allow stream if the source is YouTube (or Ytdlp if you add it)
        return Task.FromResult(video.Source == SongSource.YouTube);
    }
}