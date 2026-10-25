using Singularity.Features.Music.Models;
using Singularity.Features.Music.Resolvers;
using Singularity.Features.Music.Services;

namespace Singularity.Features.Music;

internal sealed record SearchSession(ulong UserId, IReadOnlyList<MusicTrackNew> Results);

internal readonly record struct SearchOffer(string Token, IReadOnlyList<MusicTrackNew> Results);

/// <summary>/search: finds candidates and keeps them until their menu is used. Each offer can be used once.</summary>
public sealed class MusicSearch
{
    private readonly Lock _lock = new();
    private readonly ITrackSource _source;
    private readonly MusicOptions _options;
    private readonly ExpiringCache<SearchSession> _sessions;

    internal MusicSearch(ITrackSource source, MusicOptions options, TimeProvider time)
    {
        _source = source;
        _options = options;
        _sessions = new ExpiringCache<SearchSession>(options.SearchLifetime, time);
    }

    internal async Task<SearchOffer> FindAsync(ulong userId, string query)
    {
        var results = await _source.SearchAsync(query, Math.Clamp(_options.SearchResults, 1, 25));
        if (results.Count == 0) throw new SearchException("No songs found for the provided term.");

        var token = Guid.NewGuid().ToString("N");
        lock (_lock) _sessions.Set(token, new SearchSession(userId, results));
        return new SearchOffer(token, results);
    }

    internal bool TryPeek(string token, out SearchSession session)
    {
        lock (_lock) return _sessions.TryGetFresh(token, out session);
    }

    /// <summary>Hands out the offer and forgets it, so a second click on the same menu finds nothing.</summary>
    internal bool TryTake(string token, out SearchSession session)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetFresh(token, out session)) return false;

            _sessions.Remove(token);
            return true;
        }
    }
}
