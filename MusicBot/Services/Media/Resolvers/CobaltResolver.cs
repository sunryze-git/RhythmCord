using CobaltApi;
using Microsoft.Extensions.Logging;
using MusicBot.Utilities;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;

namespace MusicBot.Services.Media.Resolvers;

public class CobaltResolver(CobaltClient cobaltClient, ILogger<CobaltResolver> logger) : IMediaResolver
{
    public string Name => "Cobalt";
    public int Priority => 1;
    
    public Task<bool> CanResolveAsync(string query)
    {
        return Task.FromResult(Uri.IsWellFormedUriString(query, UriKind.Absolute));
    }

    public async Task<IReadOnlyList<IVideo>> ResolveAsync(string query)
    {
        try
        {
            var request = new Request { url = query, audioFormat = "opus" };
            var video = await cobaltClient.GetCobaltResponseAsync(request);
            var stream = await cobaltClient.GetTunnelStreamAsync(video);
            
            return new List<IVideo> 
            { 
                new CustomSong(query, video.Title, video.Artist, 
                    new List<Thumbnail> { new(string.Empty, new Resolution()) }, stream) 
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cobalt resolution failed for: {Query}", query);
            return [];
        }
    }

    public async Task<Stream> GetStreamAsync(IVideo video)
    {
        var request = new Request { url = video.Url, audioFormat = "best" };
        var videoInfo = await cobaltClient.GetCobaltResponseAsync(request);
        return await cobaltClient.GetTunnelStreamAsync(videoInfo);
    }
}