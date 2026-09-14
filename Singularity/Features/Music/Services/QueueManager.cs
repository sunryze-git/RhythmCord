using System.Collections.Immutable;
using Singularity.Features.Music.Models;
using Singularity.Infrastructure;

namespace Singularity.Features.Music.Services;

public class QueueManager : IAsyncDisposable
{
    private readonly List<MusicTrackNew> _songQueue = [];

    internal ImmutableList<MusicTrackNew> SongQueue => [.. _songQueue];
    internal MusicTrackNew? CurrentSong => _songQueue.FirstOrDefault();

    internal void AddSong(MusicTrackNew song, bool playNext = false)
    {
        if (playNext)
            _songQueue.Insert(0, song);
        else
            _songQueue.Add(song);
    }

    internal void AddSong(IEnumerable<MusicTrackNew> songs, bool playNext = false)
    {
        if (playNext)
            _songQueue.InsertRange(0, songs);
        else
            _songQueue.AddRange(songs);
    }

    internal void RemoveCurrent()
    {
        if (_songQueue.Count == 0) return;
        var current = CurrentSong;
        if (current == null) return;

        _songQueue.Remove(current);
    }

    internal async ValueTask RemoveCurrentAsync()
    {
        if (_songQueue.Count == 0) return;
        var current = CurrentSong;
        if (current == null) return;

        _songQueue.Remove(current);
    }

    internal void Shuffle()
    {
        if (_songQueue.Count == 0) return;
        _songQueue.Shuffle();
    }

    internal void Clear()
    {
        _songQueue.Clear();
    }

    internal async ValueTask ClearAsync()
    {
        _songQueue.Clear();
    }

    internal bool IsEmpty() => _songQueue.Count == 0;

    public async ValueTask DisposeAsync()
    {
        await ClearAsync();
        GC.SuppressFinalize(this);
    }
}
