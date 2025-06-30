using Microsoft.Extensions.Logging;
using MusicBot.Services.Media.Backends;
using MusicBot.Utilities;
using YoutubeExplode.Videos;

namespace MusicBot.Services.Media.Resolvers;

public class YoutubeResolver(YoutubeBackend youtubeBackend, ILogger<YoutubeResolver> logger) : IMediaResolver
{
    public string Name => "YouTubeExplode";
    public int Priority => 98;
    
    private static readonly HashSet<string> YouTubeDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com",
        "www.youtube.com",
        "youtu.be",
        "music.youtube.com"
    };
    
    public Task<bool> CanResolveAsync(string query)
    {
        // Can resolve URLs
        if (!Uri.IsWellFormedUriString(query, UriKind.Absolute))
            return Task.FromResult(!string.IsNullOrWhiteSpace(query)); // not a URL but could be a search
        
        var uri = new Uri(query);
        return Task.FromResult(YouTubeDomains.Contains(uri.Host)); // is a youtube URL
    }

    public async Task<IReadOnlyList<CustomSong>> ResolveAsync(string query)
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
                    return playlist.Count == 0 
                        ? Array.Empty<CustomSong>() 
                        : playlist.Select(v => new CustomSong(v, query, SongSource.YouTube)).ToList();
                }

                logger.LogInformation("Resolving YouTube video: {VideoUrl}", uri);
                var video = await youtubeBackend.GetVideoAsync(uri.AbsoluteUri);
                return video == null 
                    ? Array.Empty<CustomSong>() 
                    : new List<CustomSong> { new(video, query, SongSource.YouTube) };
            }

            // Handle search query
            logger.LogInformation("Searching YouTube for: {Query}", query);
            var searchResult = await youtubeBackend.GetVideoAsync(query);
            return searchResult == null 
                ? Array.Empty<CustomSong>() 
                : new List<CustomSong> { new(searchResult, query, SongSource.YouTube) };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "YouTube resolution failed for query: {Query}", query);
            return [];
        }
    }

    public async Task<Stream> GetStreamAsync(CustomSong video)
    {
        if (video.Source != SongSource.YouTube)
            throw new Exception($"Cannot stream non-YouTube song with YouTubeResolver: {video.Title}");
        // Try to resolve to IVideo if needed
        var resolved = await youtubeBackend.GetVideoAsync(video.Url);
        if (resolved == null)
            throw new Exception($"Could not resolve YouTube video for streaming: {video.Title}");
        return await youtubeBackend.GetStreamAsync(resolved);
    }

    public async Task<bool> CanGetStreamAsync(CustomSong video)
    {
        return await Task.FromResult(video.Source == SongSource.YouTube);
    }

    private static bool IsPlaylistUrl(Uri uri)
    {
        return uri.AbsolutePath == "/playlist" || uri.Query.Contains("list=");
    }
}