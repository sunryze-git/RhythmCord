using System.Diagnostics;

using Microsoft.Extensions.Logging;

using MusicBot.Features.Media.Backends;
using MusicBot.Features.Media.Resolvers;
using MusicBot.Infrastructure;

namespace MusicBot.Features.Media;

public class MediaResolver(
    ILogger<MediaResolver> logger,
    IEnumerable<IMediaResolver> resolvers)
{
    private static readonly HashSet<string> _unresolvableHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "spotify.com",
        "open.spotify.com",
        "music.apple.com",
        "deezer.com",
        "tidal.com",
        "amazon.com"
    };

    private readonly IMediaResolver[] _resolvers = resolvers.OrderBy(r => r.Priority).ToArray();

    /// <summary>
    ///     Resolve a search term or URL to one or more CustomSong results.
    ///     The method will attempt a small pre-resolution step for known platforms
    ///     that are not directly playable by available resolvers (for example Spotify)
    ///     using an external Odesli lookup.
    /// </summary>
    public async Task<IReadOnlyList<MusicTrack>> ResolveSongsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be null or empty", nameof(query));

        logger.LogInformation("Resolving songs for query: {Query}", query);

        // log available resolvers in a single string
        var resolvers = string.Join(", ", _resolvers.Select(r => r.Name));
        logger.LogInformation("List of available resolvers:\n {Resolvers}", resolvers);

        // Try to pre-resolve if the input is a link from an unplayable host
        query = await TryConvertQueryToResolvableSourceAsync(query).ConfigureAwait(false);

        // Attempt to resolve using available resolvers
        foreach (var resolver in _resolvers)
            try
            {
                // Skip disabled resolvers
                if (!resolver.Enabled)
                    continue;

                // Check if resolver can handle the query
                if (!await resolver.CanResolveAsync(query).ConfigureAwait(false))
                    continue;

                logger.LogDebug("Attempting resolution with {ResolverName}", resolver.Name);
                var results = await resolver.ResolveAsync(query).ConfigureAwait(false);

                if (results.Count <= 0) continue;
                logger.LogInformation("Successfully resolved {Count} videos using {ResolverName}", results.Count,
                    resolver.Name);
                return results;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Resolver {ResolverName} failed for query: {Query}", resolver.Name, query);
            }

        logger.LogWarning("No resolver could handle query: {Query}", query);
        return Array.Empty<MusicTrack>();
    }

    public async Task<Stream?> ResolveStreamAsync(MusicTrack song)
    {
        var sw = Stopwatch.StartNew();
        ArgumentNullException.ThrowIfNull(song);

        foreach (var resolver in _resolvers)
        {
            if (!resolver.Enabled) continue;

            try
            {
                if (!await resolver.CanGetStreamAsync(song).ConfigureAwait(false))
                {
                    logger.LogDebug("Resolver {ResolverName} cannot get stream for song: {SongTitle}", resolver.Name,
                        song.Title);
                    continue;
                }

                logger.LogDebug("Attempting to get stream with {ResolverName}", resolver.Name);
                var stream = await resolver.GetStreamAsync(song).ConfigureAwait(false);
                logger.LogInformation("Resolved Stream in {ElapsedMilliseconds} ms.", sw.ElapsedMilliseconds);
                sw.Reset();
                return stream;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Resolver {ResolverName} failed to get stream for song: {Song}", resolver.Name,
                    song.Title);
            }
        }

        logger.LogWarning("No resolver could provide a stream for song: {Song}", song.Title);
        return null;
    }

    private static bool IsUnresolvableHost(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            return false;

        // Check host and second level host matches in set
        var host = uri.Host;
        if (_unresolvableHosts.Contains(host)) return true;

        // check for domain variants like "open.spotify.com" vs "spotify.com"
        var parts = host.Split('.');
        if (parts.Length < 2) return false;
        var lastTwo = string.Join('.', parts.Skip(parts.Length - 2));
        return _unresolvableHosts.Contains(lastTwo);
    }

    private static async Task<string> TryConvertQueryToResolvableSourceAsync(string query)
    {
        if (!IsUnresolvableHost(query))
            return query;

        // Query looks like an unresolvable platform link; try Odesli to find a playable link
        var result = await OdesliSearcher.SearchAsync(query).ConfigureAwait(false);
        if (result?.LinksByPlatform == null)
            throw new InvalidOperationException($"Cannot play this platform: {query}. No playable alternative found.");

        // Prefer SoundCloud, then YouTube, then any available link
        if (result.LinksByPlatform.TryGetValue("soundcloud", out var sc)) return sc;
        if (result.LinksByPlatform.TryGetValue("youtube", out var yt)) return yt;

        // Fallback: return first available link value
        var first = result.LinksByPlatform.Values.FirstOrDefault();
        return !string.IsNullOrEmpty(first)
            ? first
            : throw new InvalidOperationException(
                $"Cannot play this platform: {query}. No playable alternative found.");
    }
}
