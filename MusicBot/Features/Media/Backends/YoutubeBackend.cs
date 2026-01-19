using Microsoft.Extensions.Logging;

using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace MusicBot.Features.Media.Backends;

public class YoutubeBackend(ILogger<YoutubeBackend> logger)
{
    private readonly YoutubeClient _client = new();

    // Combined method to get video metadata
    internal async Task<IVideo?> GetVideoAsync(string queryOrUrl)
    {
        logger.LogInformation("Attempting to get video for: {QueryOrUrl}", queryOrUrl);

        if (VideoId.TryParse(queryOrUrl) is { } videoId)
        {
            logger.LogInformation("Input is a valid video ID/URL. Fetching directly.");
            try
            {
                return await _client.Videos.GetAsync(videoId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch video directly using ID/URL: {QueryOrUrl}", queryOrUrl);
                return null;
            }
        }

        logger.LogInformation("Input is not a direct video ID/URL. Performing search.");
        try
        {
            // Get the first result from the async enumerable without LINQ
            await using var enumerator = _client.Search.GetVideosAsync(queryOrUrl).GetAsyncEnumerator();
            if (!await enumerator.MoveNextAsync())
            {
                logger.LogWarning("Search for '{QueryOrUrl}' returned no results.", queryOrUrl);
                return null;
            }

            var searchResult = enumerator.Current;
            logger.LogInformation("Search for '{QueryOrUrl}' found video: {Title}", queryOrUrl, searchResult.Title);
            // Fetch full metadata using the ID from the search result
            return searchResult; // testing to see if this is sufficient, would save on an extra call
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed during video search or fetch for query: {QueryOrUrl}", queryOrUrl);
            return null;
        }
    }

    internal async Task<IReadOnlyList<PlaylistVideo>> GetPlaylistVideosAsync(string playlistUrlOrId)
    {
        logger.LogInformation("Getting playlist videos for {PlaylistUrlOrId}", playlistUrlOrId);
        // YoutubeExplode handles parsing playlist URLs/IDs automatically
        try
        {
            return await _client.Playlists.GetVideosAsync(playlistUrlOrId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get playlist videos for: {PlaylistUrlOrId}", playlistUrlOrId);
            return []; // Return empty list on failure
        }
    }

    internal async Task<Stream> GetStreamAsync(VideoId videoId)
    {
        logger.LogInformation("Getting stream for video: {Title}", videoId);
        try
        {
            var manifest = await _client.Videos.Streams.GetManifestAsync(videoId);

            var streamInfo = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
            if (streamInfo is null) throw new InvalidOperationException($"No audio stream found for video: {videoId}");

            return await _client.Videos.Streams.GetAsync(streamInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get stream for video: {Title}", videoId);
            throw;
        }
    }
}
