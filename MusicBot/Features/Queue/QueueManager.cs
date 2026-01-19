using System.Collections.Immutable;

using MusicBot.Infrastructure;

namespace MusicBot.Features.Queue;

public class QueueManager
{
    private readonly List<MusicTrack> _songQueue = [];

    public ImmutableList<MusicTrack> SongQueue => _songQueue.ToImmutableList();
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
        if (CurrentSong == null) return;
        _songQueue.Remove(CurrentSong);
    }

    public void Shuffle()
    {
        if (_songQueue.Count == 0) return;
        _songQueue.Shuffle();
    }

    public void Clear() => _songQueue.Clear();

    public bool IsEmpty() => _songQueue.Count == 0;
}
