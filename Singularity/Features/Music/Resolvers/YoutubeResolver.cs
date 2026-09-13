using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Singularity.Features.Music.Backends;
using Singularity.Features.Music.Models;
using YoutubeExplode.Videos;

namespace Singularity.Features.Music.Resolvers;

public class YoutubeResolver(YoutubeBackend youtubeBackend, ILogger<YoutubeResolver> logger) : IMediaResolver
{
    private static readonly HashSet<string> _youTubeDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com", "www.youtube.com", "youtu.be", "music.youtube.com"
    };

    public string Name => "YouTubeExplode";

    public Task<bool> CanResolveAsync(string query)
    {
        if (!Uri.TryCreate(query, UriKind.Absolute, out var uri))
            return Task.FromResult(!string.IsNullOrWhiteSpace(query));

        return Task.FromResult(_youTubeDomains.Contains(uri.Host));
    }

    public async Task<IReadOnlyList<MusicTrackNew>> ResolveAsync(string query)
    {
        try
        {
            if (Uri.TryCreate(query, UriKind.Absolute, out var uri) && _youTubeDomains.Contains(uri.Host))
            {
                if (IsPlaylistUrl(uri))
                {
                    logger.LogInformation("Resolving YouTube playlist: {PlaylistUrl}", uri);
                    var playlist = await youtubeBackend.GetPlaylistVideosAsync(uri.AbsoluteUri);
                    var playlistTracks = playlist.Select(v => v.FromYouTubeVideo(query)).ToList();

                    if (playlistTracks.Count > 0 && playlistTracks[0].MediaId is not null)
                    {
                        playlistTracks[0].PreResolvedStreamInfoTask =
                            youtubeBackend.GetStreamInfoAsync(new VideoId(playlistTracks[0].MediaId!));
                    }

                    return playlistTracks;
                }

                if (VideoId.TryParse(uri.AbsoluteUri) is { } videoId)
                {
                    logger.LogInformation("Resolving YouTube video concurrently: {VideoUrl}", uri);

                    var videoTask = youtubeBackend.GetVideoAsync(videoId.Value);
                    var streamInfoTask = youtubeBackend.GetStreamInfoAsync(videoId.Value);

                    await Task.WhenAll(videoTask, streamInfoTask);

                    var video = await videoTask;
                    if (video == null) return [];

                    var track = video.FromYouTubeVideo(query);
                    track.PreResolvedStreamInfoTask = streamInfoTask;

                    return [track];
                }
            }

            // Search query fallback
            logger.LogInformation("Searching YouTube for: {Query}", query);
            var searchResult = await youtubeBackend.GetVideoAsync(query);
            if (searchResult == null) return [];

            var searchTrack = searchResult.FromYouTubeVideo(query);
            searchTrack.PreResolvedStreamInfoTask = youtubeBackend.GetStreamInfoAsync(searchResult.Id);

            return [searchTrack];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "YouTube resolution failed for query: {Query}", query);
            return [];
        }
    }

    public async Task<Stream?> GetStreamAsync(MusicTrackNew track)
    {
        if (track.PreResolvedStreamInfoTask is not null)
        {
            var streamInfo = await track.PreResolvedStreamInfoTask;
            return await youtubeBackend.GetStreamFromInfoAsync(streamInfo);
        }

        var videoId = track.MediaId ?? track.Url;
        return await youtubeBackend.GetStreamAsync(videoId);
    }

    public void PreFetchStreamInfo(MusicTrackNew track)
    {
        if (track.PreResolvedStreamInfoTask is null && !string.IsNullOrEmpty(track.MediaId))
        {
            track.PreResolvedStreamInfoTask = youtubeBackend.GetStreamInfoAsync(new VideoId(track.MediaId));
            logger.LogDebug("Initiated YouTube stream info pre-fetch for: {Title}", track.Title);
        }
    }

    private static bool IsPlaylistUrl(Uri uri) =>
        uri.AbsolutePath.Equals("/playlist", StringComparison.OrdinalIgnoreCase) ||
        uri.Query.Contains("list=", StringComparison.OrdinalIgnoreCase);
}
