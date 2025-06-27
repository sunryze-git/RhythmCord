using Microsoft.Extensions.Logging;
using YoutubeExplode.Videos;
using MusicBot.Services.Media.Resolvers;
using MusicBot.Utilities;

namespace MusicBot.Services.Media;

public class MediaResolver(
    ILogger<MediaResolver> logger,
    IEnumerable<IMediaResolver> resolvers)
{
    private readonly IMediaResolver[] _resolvers = resolvers.OrderBy(r => r.Priority).ToArray();

    public async Task<IReadOnlyList<IVideo>> ResolveSongsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be null or empty", nameof(query));

        logger.LogInformation("Resolving songs for query: {Query}", query);

        // Try each resolver in priority order
        foreach (var resolver in _resolvers)
        {
            try
            {
                if (!await resolver.CanResolveAsync(query)) continue;
                logger.LogDebug("Attempting resolution with {ResolverName}", resolver.Name);
                var results = await resolver.ResolveAsync(query);

                if (results.Count <= 0) continue;
                logger.LogInformation("Successfully resolved {Count} videos using {ResolverName}", 
                    results.Count, resolver.Name);
                return results;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Resolver {ResolverName} failed for query: {Query}", 
                    resolver.Name, query);
                // Continue to next resolver
            }
        }

        logger.LogWarning("No resolver could handle query: {Query}", query);
        return [];
    }
    
    public async Task<Stream> ResolveStreamAsync(IVideo video)
    {
        // If a CustomSong, we already have the stream
        if (video is CustomSong customSong)
        {
            if (customSong.Source.CanSeek)
                customSong.Source.Position = 0;
            return customSong.Source;
        }
        
        // Try the resolvers that can provide streams
        foreach (var resolver in _resolvers)
        {
            try
            {
                return await resolver.GetStreamAsync(video);
            }
            catch (NotSupportedException)
            {
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Stream resolution failed with {ResolverName}", resolver.Name);
            }
        }

        throw new NotSupportedException($"No resolver could provide stream for video: {video.Title}");
    }
}