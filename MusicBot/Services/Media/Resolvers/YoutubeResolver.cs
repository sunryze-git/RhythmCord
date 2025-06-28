using Microsoft.Extensions.Logging;
using MusicBot.Services.Media.Backends;
using YoutubeExplode.Videos;

namespace MusicBot.Services.Media.Resolvers;

public class YoutubeResolver(YoutubeBackend youtubeBackend, ILogger<YoutubeResolver> logger) : IMediaResolver
{
    public string Name => "YouTubeExplode";
    public int Priority => 4;
    public Task<bool> CanResolveAsync(string query)
    {
        // Can resolve URLs
        if (Uri.IsWellFormedUriString(query, UriKind.Absolute))
        {
            var uri = new Uri(query);
            var isYouTube = uri.Host.Contains("youtube.com") || uri.Host.Contains("youtu.be");
            return Task.FromResult(isYouTube);
        }

        // Can also resolve search queries (any non-URL string)
        return Task.FromResult(!string.IsNullOrWhiteSpace(query));
    }

    public async Task<IReadOnlyList<IVideo>> ResolveAsync(string query)
    {
        try
        {
            // Handle URL
            if (Uri.IsWellFormedUriString(query, UriKind.Absolute))
            {
                var uri = new Uri(query);

                if (IsPlaylistUrl(uri))
                {
                    logger.LogInformation("Resolving YouTube playlist: {PlaylistUrl}", uri);
                    var playlist = await youtubeBackend.GetPlaylistVideosAsync(uri.AbsoluteUri);
                    return playlist.Count == 0 ? Array.Empty<IVideo>() : playlist;
                }

                logger.LogInformation("Resolving YouTube video: {VideoUrl}", uri);
                var video = await youtubeBackend.GetVideoAsync(uri.AbsoluteUri);
                return video == null ? Array.Empty<IVideo>() : new List<IVideo> { video };
            }

            // Handle search query
            logger.LogInformation("Searching YouTube for: {Query}", query);
            var searchResult = await youtubeBackend.GetVideoAsync(query);
            return searchResult == null ? Array.Empty<IVideo>() : new List<IVideo> { searchResult };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "YouTube resolution failed for query: {Query}", query);
            return [];
        }
    }

    public Task<Stream> GetStreamAsync(IVideo video)
    {
        throw new NotSupportedException("You should use the Cobalt API to get streams.");
    }
    
    private static bool IsPlaylistUrl(Uri uri)
    {
        return uri.AbsolutePath == "/playlist" || uri.Query.Contains("list=");
    }
}