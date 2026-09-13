using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Resolvers;

namespace Singularity.Features.Music.Services;

public class MediaResolver(
    YoutubeResolver youtubeResolver,
    YtdlpResolver ytdlpResolver,
    ILogger<MediaResolver> logger)
{
    public async Task<IReadOnlyList<MusicTrackNew>> ResolveSongsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be null or empty", nameof(query));

        logger.LogInformation("Resolving songs for query: {Query}", query);

        // 1. Try YoutubeExplode first
        if (await youtubeResolver.CanResolveAsync(query))
        {
            try
            {
                var results = await youtubeResolver.ResolveAsync(query);
                if (results.Count > 0) return results;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "YouTubeExplode failed for query: {Query}, falling back to yt-dlp.", query);
            }
        }

        // 2. Fallback to yt-dlp for non-YouTube links or failed YouTube requests
        try
        {
            logger.LogInformation("Attempting resolution with YT-DLP for query: {Query}", query);
            return await ytdlpResolver.ResolveAsync(query);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "YT-DLP resolution failed for query: {Query}", query);
            return [];
        }
    }

    public async Task<Stream?> ResolveStreamAsync(MusicTrackNew track)
    {
        var sw = Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(track);

        try
        {
            IMediaResolver resolver = track.Source == SongSource.YouTube
                ? youtubeResolver
                : ytdlpResolver;

            var stream = await resolver.GetStreamAsync(track);
            logger.LogInformation("Resolved stream for '{Title}' in {ElapsedMs} ms.", track.Title, sw.ElapsedMilliseconds);
            return stream;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve stream for song: {Song}", track.Title);
            return null;
        }
    }

    public void PreFetchStreamInfo(MusicTrackNew track)
    {
        if (track.Source == SongSource.YouTube)
        {
            youtubeResolver.PreFetchStreamInfo(track);
        }
    }
}
