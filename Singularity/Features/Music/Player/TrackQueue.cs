using System.Diagnostics.CodeAnalysis;
using Singularity.Features.Music.Models;
using Singularity.Infrastructure;

namespace Singularity.Features.Music.Player;

/// <summary>
///     The tracks waiting to play. NOT thread-safe: it is owned by a single <see cref="GuildPlayer" /> loop, which is the
///     only thing that touches it. The track being played is not in here.
/// </summary>
internal sealed class TrackQueue(int capacity)
{
    private readonly List<MusicTrackNew> _items = [];

    internal int Count => _items.Count;
    internal IReadOnlyList<MusicTrackNew> Items => _items;

    /// <summary>Adds songs up to the capacity and returns how many were accepted (the first ones, in order).</summary>
    internal int Add(IReadOnlyList<MusicTrackNew> songs, bool playNext)
    {
        var room = capacity - _items.Count;
        if (room <= 0) return 0;

        var accepted = songs.Count <= room ? songs : songs.Take(room).ToList();
        if (playNext)
            _items.InsertRange(0, accepted);
        else
            _items.AddRange(accepted);

        return accepted.Count;
    }

    internal MusicTrackNew? PeekNext() => _items.FirstOrDefault();

    internal bool TryDequeue([NotNullWhen(true)] out MusicTrackNew? track)
    {
        if (_items.Count == 0)
        {
            track = null;
            return false;
        }

        track = _items[0];
        _items.RemoveAt(0);
        return true;
    }

    internal void Shuffle() => _items.Shuffle();
    internal void Clear() => _items.Clear();
}
