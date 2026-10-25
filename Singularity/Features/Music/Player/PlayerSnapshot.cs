using System.Collections.Immutable;
using Singularity.Features.Music.Models;

namespace Singularity.Features.Music.Player;

/// <summary>An immutable view of a player, safe to read from any thread without waiting on the player loop.</summary>
internal sealed record PlayerSnapshot(
    MusicTrackNew? Current,
    ImmutableList<MusicTrackNew> Pending,
    LoopMode Loop,
    int SkipVotes,
    bool Closing)
{
    internal static readonly PlayerSnapshot Empty = new(null, [], LoopMode.Off, 0, false);

    internal bool Looping => Loop != LoopMode.Off;

    internal bool HasWork => Current is not null || !Pending.IsEmpty;
}

internal readonly record struct EnqueueResult(IReadOnlyList<MusicTrackNew> Accepted);
