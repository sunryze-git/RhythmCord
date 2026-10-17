using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Resolvers;
using Singularity.Infrastructure;
using Singularity.Observability;

namespace Singularity.Features.Music.Services;

/// <summary>What the player needs from the media layer. Lets the player be tested without any network or process.</summary>
internal interface ITrackSource
{
    /// <summary>Finds tracks for a query. Empty means nothing matched; a failure to look is thrown.</summary>
    Task<IReadOnlyList<MusicTrackNew>> ResolveSongsAsync(string query);

    /// <summary>Opens the track's audio, or throws <see cref="StreamUnavailableException" /> saying why it can't.</summary>
    Task<Stream> ResolveStreamAsync(MusicTrackNew track);

    void PreFetch(MusicTrackNew track);
}

/// <summary>
///     Tries the registered resolvers in the configured order (Music:ResolverOrder). The first one that finds something
///     wins; streaming prefers the resolver that produced the track and falls back to the others.
///     "Nothing found" and "could not look" are different outcomes and are reported differently.
/// </summary>
internal sealed class MediaResolver : ITrackSource
{
    private readonly IReadOnlyList<IMediaResolver> _resolvers;
    private readonly BotMetrics _metrics;
    private readonly ILogger<MediaResolver> _logger;

    public MediaResolver(IEnumerable<IMediaResolver> resolvers, MusicOptions options, BotMetrics metrics, ILogger<MediaResolver> logger)
    {
        _metrics = metrics;
        _logger = logger;
        _resolvers = Order(resolvers.ToList(), options.EffectiveResolverOrder, logger);
    }

    internal IReadOnlyList<IMediaResolver> Resolvers => _resolvers;

    public async Task<IReadOnlyList<MusicTrackNew>> ResolveSongsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new SearchException("The search query cannot be empty.");

        _logger.LogDebug("Resolving songs for query: {Query}", query);
        _metrics.ResolveRequested();
        var sw = Stopwatch.StartNew();

        var failures = new List<Exception>();
        var anyAnswered = false; // at least one resolver ran to completion (even if it found nothing)

        foreach (var resolver in _resolvers)
        {
            try
            {
                if (!await resolver.CanResolveAsync(query)) continue;

                var results = await resolver.ResolveAsync(query);
                anyAnswered = true;
                if (results.Count == 0) continue;

                _metrics.ResolvedBy(resolver.Name);
                _metrics.ResolveTime(sw.ElapsedMilliseconds);
                return results;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not SearchException)
            {
                failures.Add(ex);
                _logger.LogWarning(ex, "{Resolver} failed for query '{Query}'; trying the next resolver.", resolver.Name, query);
            }
        }

        _metrics.ResolveTime(sw.ElapsedMilliseconds);

        // Nobody could even look: that is an outage, not "no results". Say so, with the most telling reason.
        if (!anyAnswered && failures.Count > 0)
        {
            _metrics.Error("lookup.failed");
            throw new LookupFailedException(FailureDescriber.Describe(failures[^1]), failures[^1]);
        }

        _metrics.ResolveNoResults();
        return [];
    }

    public async Task<Stream> ResolveStreamAsync(MusicTrackNew track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var sw = Stopwatch.StartNew();

        // The resolver that produced the track goes first, then every other resolver able to stream it.
        var candidates = _resolvers.Where(r => r.Source == track.Source && r.CanStream(track))
            .Concat(_resolvers.Where(r => r.Source != track.Source && r.CanStream(track)))
            .ToList();

        Exception? last = null;
        for (var i = 0; i < candidates.Count; i++)
        {
            var resolver = candidates[i];
            try
            {
                var stream = await resolver.GetStreamAsync(track)
                             ?? throw new ResolverFailedException($"{resolver.Name} returned no stream");

                _metrics.StreamTime(sw.ElapsedMilliseconds);
                _logger.LogDebug("Resolved stream for '{Title}' via {Resolver} in {ElapsedMs} ms.", track.Title, resolver.Name, sw.ElapsedMilliseconds);
                return stream;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                var hasFallback = i + 1 < candidates.Count;
                if (hasFallback) _metrics.StreamFallback();

                _logger.Log(hasFallback ? LogLevel.Warning : LogLevel.Error, ex,
                    "{Resolver} could not stream '{Title}'{Next}", resolver.Name, track.Title,
                    hasFallback ? $", falling back to {candidates[i + 1].Name}." : ".");
            }
        }

        _metrics.StreamTime(sw.ElapsedMilliseconds);
        throw new StreamUnavailableException(
            last is null ? "there is no way to play this kind of source" : FailureDescriber.Describe(last), last);
    }

    public void PreFetch(MusicTrackNew track)
    {
        foreach (var resolver in _resolvers.Where(r => r.Source == track.Source))
        {
            try
            {
                resolver.PreFetch(track);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Pre-fetch failed for '{Title}'.", track.Title);
            }
        }
    }

    // Configured names first (in that order); resolvers the config forgot to mention stay available at the end.
    internal static List<IMediaResolver> Order(List<IMediaResolver> all, IReadOnlyList<string> configured, ILogger logger)
    {
        var ordered = new List<IMediaResolver>();

        foreach (var name in configured)
        {
            var match = all.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match is null) logger.LogWarning("Music:ResolverOrder lists unknown resolver '{Name}'; ignoring it.", name);
            else if (!ordered.Contains(match)) ordered.Add(match);
        }

        ordered.AddRange(all.Where(r => !ordered.Contains(r)));
        return ordered;
    }
}
