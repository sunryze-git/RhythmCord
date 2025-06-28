using MusicBot.Services.Media.Backends;
using YoutubeExplode.Videos;

namespace MusicBot.Services.Media.Resolvers;

public class YtdlpResolver(DlpBackend dlpService) : IMediaResolver
{
    public string Name => "YT-DLP";
    public int Priority => 99;
    public Task<bool> CanResolveAsync(string query)
    {
        return Task.FromResult(Uri.IsWellFormedUriString(query, UriKind.Absolute));
    }

    public async Task<IReadOnlyList<IVideo>> ResolveAsync(string query)
    {
        var results = await dlpService.GetMetadataAsync(query);
        return results;
    }

    public Task<Stream> GetStreamAsync(IVideo video)
    {
        throw new NotSupportedException("Stream is already included with the video metadata.");
    }
}