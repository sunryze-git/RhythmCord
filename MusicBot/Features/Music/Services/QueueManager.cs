using System.Collections.Immutable;
using MusicBot.Features.Music.Models;
using MusicBot.Infrastructure;

namespace MusicBot.Features.Music.Services;

public class QueueManager : IAsyncDisposable
{
    private readonly List<MusicTrackNew> _songQueue = [];

    public ImmutableList<MusicTrackNew> SongQueue => [.. _songQueue];
    public MusicTrackNew? CurrentSong => _songQueue.FirstOrDefault();

    public void AddSong(MusicTrackNew song, bool playNext = false)
    {
        if (playNext)
            _songQueue.Insert(0, song);
        else
            _songQueue.Add(song);
    }

    public void AddSong(IEnumerable<MusicTrackNew> songs, bool playNext = false)
    {
        if (playNext)
            _songQueue.InsertRange(0, songs);
        else
            _songQueue.AddRange(songs);
    }

    public void RemoveCurrent()
    {
        if (_songQueue.Count == 0) return;
        var current = CurrentSong;
        if (current == null) return;

        _songQueue.Remove(current);
    }

    public async ValueTask RemoveCurrentAsync()
    {
        if (_songQueue.Count == 0) return;
        var current = CurrentSong;
        if (current == null) return;

        _songQueue.Remove(current);
    }

    public void Shuffle()
    {
        if (_songQueue.Count == 0) return;
        _songQueue.Shuffle();
    }

    public void Clear()
    {
        _songQueue.Clear();
    }

    public async ValueTask ClearAsync()
    {
        _songQueue.Clear();
    }

    public bool IsEmpty() => _songQueue.Count == 0;

    public async ValueTask DisposeAsync()
    {
        await ClearAsync();
        GC.SuppressFinalize(this);
    }
}
