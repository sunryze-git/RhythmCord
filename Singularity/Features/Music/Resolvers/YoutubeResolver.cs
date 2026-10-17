using System.Web;
using Microsoft.Extensions.Logging;
using Singularity.Features.Music.Backends;
using Singularity.Features.Music.Models;
using Singularity.Infrastructure;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace Singularity.Features.Music.Resolvers;

internal class YoutubeResolver : IMediaResolver
{
    private static readonly HashSet<string> YouTubeDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com",
        "youtube-nocookie.com", "www.youtube-nocookie.com", "youtu.be", "www.youtu.be"
    };

    private readonly YoutubeBackend _backend;
    private readonly MusicOptions _options;
    private readonly ILogger<YoutubeResolver> _logger;

    // Stream manifests, started early so playback begins sooner. Their URLs expire, hence the lifetime.
    private readonly ExpiringCache<Task<IStreamInfo>> _streams;

    public YoutubeResolver(YoutubeBackend backend, MusicOptions options, TimeProvider time, ILogger<YoutubeResolver> logger)
    {
        _backend = backend;
        _options = options;
        _logger = logger;
        _streams = new ExpiringCache<Task<IStreamInfo>>(options.StreamInfoLifetime, time);
    }

    public string Name => "youtube";
    public SongSource Source => SongSource.YouTube;

    public Task<bool> CanResolveAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult(false);

        // Plain text is a search; URLs must point at YouTube.
        return Task.FromResult(!QueryUtil.TryGetWebUri(query, out var uri) || YouTubeDomains.Contains(uri.Host));
    }

    public async Task<IReadOnlyList<MusicTrackNew>> ResolveAsync(string query)
    {
        // Failures propagate: MediaResolver logs them and decides whether to try the next resolver.
        if (!QueryUtil.TryGetWebUri(query, out var uri))
            return await ResolveSearchAsync(query);

        if (!YouTubeDomains.Contains(uri.Host)) return [];

        if (IsPlaylistUrl(uri))
        {
            _logger.LogDebug("Resolving YouTube playlist: {PlaylistUrl}", uri);
            var playlist = await _backend.GetPlaylistVideosAsync(uri.AbsoluteUri, _options.MaxPlaylistTracks);
            var tracks = playlist.Select(v => FromVideo(v, query)).ToList();

            if (tracks.Count > 0) Warm(tracks[0].MediaId);
            return tracks;
        }

        if (VideoId.TryParse(uri.AbsoluteUri) is { } videoId)
        {
            _logger.LogDebug("Resolving YouTube video concurrently: {VideoUrl}", uri);

            // Fetch metadata and the stream manifest in parallel. A manifest failure (e.g. a live stream)
            // is not fatal here: playback re-resolves on demand and the chain can fall back to yt-dlp.
            Warm(videoId.Value);

            var video = await _backend.GetVideoAsync(videoId);
            return video == null ? [] : [FromVideo(video, query)];
        }

        return [];
    }

    public bool CanStream(MusicTrackNew track) => track.Source == SongSource.YouTube;

    public async Task<Stream?> GetStreamAsync(MusicTrackNew track)
    {
        var videoId = track.MediaId ?? track.Url;

        if (track.MediaId is { } id && _streams.TryGetFresh(id, out var pending) && pending is { IsFaulted: false, IsCanceled: false })
        {
            try
            {
                return await _backend.GetStreamFromInfoAsync(await pending);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Pre-resolved stream for '{Title}' failed, resolving again.", track.Title);
                _streams.Remove(id);
            }
        }

        return await _backend.GetStreamAsync(videoId);
    }

    public void PreFetch(MusicTrackNew track) => Warm(track.MediaId);

    // Starts (once per lifetime) the stream manifest fetch for a video, in the background.
    private void Warm(string? videoId)
    {
        if (string.IsNullOrEmpty(videoId)) return;
        if (_streams.TryGetFresh(videoId, out var existing) && existing is { IsFaulted: false, IsCanceled: false }) return;

        var task = _backend.GetStreamInfoAsync(new VideoId(videoId));
        task.Observe(_logger, $"stream info pre-fetch for {videoId}");
        _streams.Set(videoId, task);
        _logger.LogDebug("Started YouTube stream info pre-fetch for {VideoId}.", videoId);
    }

    private async Task<IReadOnlyList<MusicTrackNew>> ResolveSearchAsync(string query)
    {
        var result = await _backend.SearchFirstVideoAsync(query);
        if (result == null) return [];

        Warm(result.Id.Value);
        return [FromVideo(result, query)];
    }

    private static MusicTrackNew FromVideo(IVideo video, string query) => new()
    {
        Query = query,
        Url = video.Url,
        Title = video.Title,
        Author = video.Author.ChannelTitle,
        Duration = video.Duration,
        ThumbnailUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url,
        Source = SongSource.YouTube,
        MediaId = video.Id.Value
    };

    private static bool IsPlaylistUrl(Uri uri)
    {
        if (uri.AbsolutePath.Equals("/playlist", StringComparison.OrdinalIgnoreCase)) return true;

        // Auto-generated "mix"/radio lists (RD...) are endless and not real playlists: play just the video.
        var list = HttpUtility.ParseQueryString(uri.Query)["list"];
        return !string.IsNullOrEmpty(list) && !list.StartsWith("RD", StringComparison.OrdinalIgnoreCase);
    }
}
