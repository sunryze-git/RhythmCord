namespace Singularity.Features.Music;

/// <summary>Music tunables, bound from the "Music" configuration section (env: Music__MaxQueueLength, ...).</summary>
public sealed class MusicOptions
{
    /// <summary>Most tracks that can be waiting in one server's queue.</summary>
    public int MaxQueueLength { get; init; } = 500;

    /// <summary>Most tracks taken from a playlist in one /play.</summary>
    public int MaxPlaylistTracks { get; init; } = 200;

    public int QueuePageSize { get; init; } = 10;

    /// <summary>Candidates offered by /search (Discord allows at most 25 in a menu).</summary>
    public int SearchResults { get; init; } = 5;

    /// <summary>How long a /search menu keeps working.</summary>
    public TimeSpan SearchLifetime { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the bot waits in an empty-queue session before leaving.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long the bot stays when nobody else is in its voice channel.</summary>
    public TimeSpan AloneTimeout { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan AloneCheckInterval { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan PlayCooldown { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long a pre-fetched YouTube stream manifest is trusted (the URLs inside expire).</summary>
    public TimeSpan StreamInfoLifetime { get; init; } = TimeSpan.FromMinutes(20);

    public TimeSpan YtdlpTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>EBU R128-style loudness normalisation (costs ~1% of a core per playing stream).</summary>
    public bool Normalize { get; init; } = true;

    /// <summary>
    ///     Order in which resolvers are tried. Names: "youtube", "ytdlp". Leave empty for the default (<see cref="EffectiveResolverOrder" />).
    ///     The default is deliberately not stored here: the configuration binder appends to non-empty array defaults.
    /// </summary>
    public string[] ResolverOrder { get; init; } = [];

    public IReadOnlyList<string> EffectiveResolverOrder => ResolverOrder.Length > 0 ? ResolverOrder : ["youtube", "ytdlp"];
}
