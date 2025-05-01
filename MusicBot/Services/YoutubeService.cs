using Microsoft.Extensions.Logging;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace MusicBot.Services;

public class YoutubeService(ILogger<YoutubeService> logger)
{
    private readonly YoutubeClient _client = new();
    
    // Combined method to get video metadata
    internal async Task<Video?> GetVideoAsync(string queryOrUrl)
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
            // Simplify search: take the first result
            var videos = await _client.Search.GetVideosAsync(queryOrUrl);
            var searchResult = videos.FirstOrDefault();
            if (searchResult == null)
            {
                logger.LogWarning("Search for '{QueryOrUrl}' returned no results.", queryOrUrl);
                return null;
            }
            logger.LogInformation("Search for '{QueryOrUrl}' found video: {Title}", queryOrUrl, searchResult.Title);
            // Fetch full metadata using the ID from the search result
            return await _client.Videos.GetAsync(searchResult.Id);
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

    internal async Task<Stream?> GetAudioStreamAsync(IVideo video)
    {
        logger.LogInformation("Getting Opus audio stream for video: {VideoTitle}", video.Title);
        try
        {
            var streamManifest = await _client.Videos.Streams.GetManifestAsync(video.Id);

            var streamInfo = streamManifest
                .GetAudioOnlyStreams()
                .Where(s => s.AudioCodec.Equals("opus", StringComparison.OrdinalIgnoreCase)) // Hardcoded to opus
                .TryGetWithHighestBitrate();

            if (streamInfo == null)
            {
                logger.LogWarning("Could not find an Opus audio stream for video {VideoTitle}. Available codecs: {Codecs}",
                    video.Title,
                    string.Join(", ", streamManifest.GetAudioOnlyStreams().Select(s => s.AudioCodec).Distinct()));
                return null; // Indicate failure
            }

            logger.LogInformation("Found Opus audio stream: Bitrate={Bitrate}", streamInfo.Bitrate);
            return await _client.Videos.Streams.GetAsync(streamInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get audio stream for video: {VideoTitle}", video.Title);
            return null;
        }
    }
}