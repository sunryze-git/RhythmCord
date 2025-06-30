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

    // Primary entry point to finding an IVideo object based on a query link or search term.
    public async Task<IReadOnlyList<CustomSong>> ResolveSongsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be null or empty", nameof(query));

        logger.LogInformation("Resolving songs for query: {Query}", query);

        // Pre-resolve step: convert known-unresolvable links to a playable one using Odesli
        if (IsUnresolvableMusicService(query))
        {
            var odesliResult = await Backends.OdesliSearcher.SearchAsync(query);
            if (odesliResult is { LinksByPlatform: not null })
            {
                if (odesliResult.LinksByPlatform.TryGetValue("soundcloud", out var scUrl))
                {
                    logger.LogInformation("Pre-resolved {Query} to SoundCloud: {Url}", query, scUrl);
                    query = scUrl;
                }
                else if (odesliResult.LinksByPlatform.TryGetValue("youtube", out var ytUrl))
                {
                    logger.LogInformation("Pre-resolved {Query} to YouTube: {Url}", query, ytUrl);
                    query = ytUrl;
                }
                else
                {
                    throw new Exception($"Cannot play this platform: {query}. No playable alternative found.");
                }
            }
            else
            {
                throw new Exception($"Cannot play this platform: {query}. No playable alternative found.");
            }
        }

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

    public async Task<Stream?> ResolveStreamAsync(CustomSong song)
    {
        foreach (var resolver in _resolvers)
        {
            try
            {
                if (!await resolver.CanGetStreamAsync(song))
                {
                    logger.LogDebug("Resolver {ResolverName} cannot get stream for song: {SongTitle}", resolver.Name, song.Title);
                    continue;
                }
                logger.LogDebug("Attempting to get stream with {ResolverName}", resolver.Name);
                return await resolver.GetStreamAsync(song);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Resolver {ResolverName} failed to get stream for song: {Song}", resolver.Name, song.Title);
                // Continue to next resolver
            }
        }
        logger.LogWarning("No resolver could provide a stream for song: {Song}", song.Title);
        return null;
    }

    private static bool IsUnresolvableMusicService(string url)
    {
        // Add more platforms as needed
        return url.Contains("spotify.com") || url.Contains("apple.com") || url.Contains("deezer.com") || url.Contains("tidal.com") || url.Contains("amazon.com");
    }
}