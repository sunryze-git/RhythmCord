using System.Collections.Immutable;

using MusicBot.Infrastructure;

namespace MusicBot.Features.Queue;

public class QueueManager : IAsyncDisposable
{
    private readonly List<MusicTrack> _songQueue = [];

    public ImmutableList<MusicTrack> SongQueue => [.. _songQueue];
    public MusicTrack? CurrentSong => _songQueue.FirstOrDefault();

    public void AddSong(MusicTrack song, bool playNext = false)
    {
        if (playNext)
            _songQueue.Insert(0, song);
        else
            _songQueue.Add(song);
    }

    public void AddSong(IEnumerable<MusicTrack> songs, bool playNext = false)
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
        _ = current.DisposeAsync();
    }

    public async ValueTask RemoveCurrentAsync()
    {
        if (_songQueue.Count == 0) return;
        var current = CurrentSong;
        if (current == null) return;

        _songQueue.Remove(current);
        await current.DisposeAsync();
    }

    public void Shuffle()
    {
        if (_songQueue.Count == 0) return;
        _songQueue.Shuffle();
    }

    public void Clear()
    {
        foreach (var track in _songQueue)
        {
            _ = track.DisposeAsync();
        }
        _songQueue.Clear();
    }

    public async ValueTask ClearAsync()
    {
        foreach (var track in _songQueue)
        {
            await track.DisposeAsync();
        }
        _songQueue.Clear();
    }

    public bool IsEmpty() => _songQueue.Count == 0;

    public async ValueTask DisposeAsync()
    {
        await ClearAsync();
        GC.SuppressFinalize(this);
    }
}
