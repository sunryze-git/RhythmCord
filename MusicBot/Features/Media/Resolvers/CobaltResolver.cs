using CobaltApi;

using Microsoft.Extensions.Logging;

using MusicBot.Infrastructure;

namespace MusicBot.Features.Media.Resolvers;

public class CobaltResolver(CobaltClient cobaltClient, ILogger<CobaltResolver> logger) : IMediaResolver
{
    public string Name => "Cobalt";
    public int Priority => 97;
    public bool Enabled => false;

    // Cobalt only supports direct URLs that are not files.
    public Task<bool> CanResolveAsync(string query)
    {
        if (!Uri.IsWellFormedUriString(query, UriKind.Absolute))
            return Task.FromResult(false); // not a URL
        var uri = new Uri(query);
        return Task.FromResult(!uri.IsFile); // Cobalt only resolves URLs, not files
    }

    public async Task<IReadOnlyList<MusicTrack>> ResolveAsync(string query)
    {
        try
        {
            var request = new Request(query, audioFormat: "best");
            var video = await cobaltClient.GetCobaltResponseAsync(request);

            return new List<MusicTrack>
            {
                new(query, query, video.Title, video.Artist, TimeSpan.Zero, string.Empty, SongSource.Cobalt)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cobalt resolution failed for: {Query}", query);
            return [];
        }
    }

    public async Task<Stream> GetStreamAsync(MusicTrack video)
    {
        if (video.Source is not SongSource.Cobalt and not SongSource.YouTube and not SongSource.SoundCloud)
            throw new Exception($"Cannot stream non-Cobalt song with CobaltResolver: {video.Title}");
        var request = new Request(video.Url, audioFormat: "best");
        var videoInfo = await cobaltClient.GetCobaltResponseAsync(request);
        return await cobaltClient.GetTunnelStreamAsync(videoInfo);
    }

    public async Task<bool> CanGetStreamAsync(MusicTrack video) =>
        await Task.FromResult(video.Source is SongSource.Cobalt or SongSource.YouTube or SongSource.SoundCloud);
}
