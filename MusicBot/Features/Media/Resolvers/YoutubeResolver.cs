using System.Diagnostics;

using Microsoft.Extensions.Logging;

using MusicBot.Features.Media.Backends;
using MusicBot.Infrastructure;

namespace MusicBot.Features.Media.Resolvers;

public class YoutubeResolver(YoutubeBackend youtubeBackend, ILogger<YoutubeResolver> logger) : IMediaResolver
{
    private static readonly HashSet<string> _youTubeDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com",
        "www.youtube.com",
        "youtu.be",
        "music.youtube.com"
    };

    public bool Enabled => true;
    public string Name => "YouTubeExplode";
    public int Priority => 98;

    public Task<bool> CanResolveAsync(string query)
    {
        // Can resolve URLs
        if (!Uri.IsWellFormedUriString(query, UriKind.Absolute))
            return Task.FromResult(!string.IsNullOrWhiteSpace(query)); // not a URL but could be a search

        var uri = new Uri(query);
        return Task.FromResult(_youTubeDomains.Contains(uri.Host)); // is a youtube URL
    }

    public async Task<IReadOnlyList<MusicTrack>> ResolveAsync(string query)
    {
        var sw = Stopwatch.StartNew();
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

                    logger.LogInformation("Resolved {Count} tracks from playlist in {ElapsedMilliseconds} ms",
                        playlist.Count, sw.ElapsedMilliseconds);
                    sw.Reset();

                    return playlist.Count == 0
                        ? Array.Empty<MusicTrack>()
                        : playlist.Select(v => new MusicTrack(v, query, SongSource.YouTube)).ToList();
                }

                logger.LogInformation("Resolving YouTube video: {VideoUrl}", uri);
                var video = await youtubeBackend.GetVideoAsync(uri.AbsoluteUri);
                logger.LogInformation("Resolved video in {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
                sw.Reset();

                return video == null
                    ? Array.Empty<MusicTrack>()
                    : new List<MusicTrack> { new(video, query, SongSource.YouTube) };
            }

            // Handle search query
            logger.LogInformation("Searching YouTube for: {Query}", query);
            var searchResult = await youtubeBackend.GetVideoAsync(query);
            logger.LogInformation("Search resolved in {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            sw.Reset();
            return searchResult == null
                ? Array.Empty<MusicTrack>()
                : new List<MusicTrack> { new(searchResult, query, SongSource.YouTube) };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "YouTube resolution failed for query: {Query}", query);
            return [];
        }
    }

    public async Task<Stream> GetStreamAsync(MusicTrack video)
    {
        // If we already have the IVideo, use it directly
        if (video.ResolvedVideo is not null) return await youtubeBackend.GetStreamAsync(video.ResolvedVideo.Id);

        // Resolve to the video if we don't have it yet
        var resolved = await youtubeBackend.GetVideoAsync(video.Url);
        if (resolved is not null) return await youtubeBackend.GetStreamAsync(resolved.Id);

        throw new InvalidOperationException($"Could not resolve YouTube video for streaming: {video.Title}");
    }

    public async Task<bool> CanGetStreamAsync(MusicTrack video) =>
        await Task.FromResult(video.Source == SongSource.YouTube);

    private static bool IsPlaylistUrl(Uri uri) => uri.AbsolutePath == "/playlist" || uri.Query.Contains("list=") ||
                                                  uri.Query.Contains("&list=");
}
