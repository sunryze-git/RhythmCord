using Microsoft.Extensions.Logging;
using YoutubeExplode;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace Singularity.Features.Music.Backends;

internal class YoutubeBackend(ILogger<YoutubeBackend> logger)
{
    private readonly YoutubeClient _client = new();

    internal async Task<IVideo?> GetVideoAsync(VideoId videoId)
    {
        logger.LogDebug("Fetching video metadata for {VideoId}", videoId.Value);
        return await _client.Videos.GetAsync(videoId);
    }

    internal async Task<IVideo?> SearchFirstVideoAsync(string query)
    {
        logger.LogDebug("Searching YouTube for: {Query}", query);
        await foreach (var result in _client.Search.GetVideosAsync(query))
        {
            logger.LogDebug("Search for '{Query}' found video: {Title}", query, result.Title);
            return result;
        }

        logger.LogDebug("Search for '{Query}' returned no results.", query);
        return null;
    }

    internal async Task<IReadOnlyList<IVideo>> SearchVideosAsync(string query, int limit)
    {
        logger.LogDebug("Searching YouTube for up to {Limit} results: {Query}", limit, query);
        var results = new List<IVideo>();
        await foreach (var result in _client.Search.GetVideosAsync(query))
        {
            results.Add(result);
            if (results.Count >= limit) break;
        }

        return results;
    }

    internal async Task<IReadOnlyList<PlaylistVideo>> GetPlaylistVideosAsync(string playlistUrlOrId, int maxVideos)
    {
        logger.LogDebug("Getting playlist videos for {PlaylistUrlOrId}", playlistUrlOrId);
        var videos = new List<PlaylistVideo>();
        try
        {
            await foreach (var video in _client.Playlists.GetVideosAsync(playlistUrlOrId))
            {
                videos.Add(video);
                if (videos.Count >= maxVideos) break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && videos.Count > 0)
        {
            // A playlist that fails part-way still yields what was read; one that fails at once is a real failure.
            logger.LogWarning(ex, "Playlist {PlaylistUrlOrId} could only be read partly ({Count} videos).", playlistUrlOrId, videos.Count);
        }

        return videos;
    }

    /// <summary>
    ///     The first YouTube request after startup is ~2s slower (JIT, TLS, player-script fetch). Doing one throwaway
    ///     request in the background means the first real /play does not pay for it. Failures are not important here.
    /// </summary>
    internal async Task WarmUpAsync()
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await GetStreamInfoAsync("dQw4w9WgXcQ");
            logger.LogDebug("YouTube warm-up finished in {ElapsedMs} ms.", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "YouTube warm-up failed (ignored).");
        }
    }

    internal async Task<IStreamInfo> GetStreamInfoAsync(VideoId videoId)
    {
        logger.LogDebug("Fetching stream manifest for video: {VideoId}", videoId.Value);
        var manifest = await _client.Videos.Streams.GetManifestAsync(videoId);
        var streamInfo = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();

        return streamInfo ?? throw new InvalidOperationException($"No audio stream found for video: {videoId.Value}");
    }

    internal async Task<Stream> GetStreamFromInfoAsync(IStreamInfo streamInfo) =>
        await _client.Videos.Streams.GetAsync(streamInfo);

    internal async Task<Stream> GetStreamAsync(VideoId videoId)
    {
        var streamInfo = await GetStreamInfoAsync(videoId);
        return await GetStreamFromInfoAsync(streamInfo);
    }
}
