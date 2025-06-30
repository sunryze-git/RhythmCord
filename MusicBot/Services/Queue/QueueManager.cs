using System.Collections.Immutable;
using MusicBot.Utilities;
using YoutubeExplode.Videos;

namespace MusicBot.Services.Queue;

public class QueueManager
{
    private readonly List<CustomSong> _songQueue = [];

    public ImmutableList<CustomSong> SongQueue => _songQueue.ToImmutableList();
    public CustomSong? CurrentSong => _songQueue.FirstOrDefault();
    
    public void AddSong(CustomSong song, bool playNext = false)
    {
        if (playNext)
        {
            _songQueue.Insert(0, song);
        }
        else
        {
            _songQueue.Add(song);
        }
    }
    
    public void AddSong(IEnumerable<CustomSong> songs, bool playNext = false)
    {
        if (playNext)
        {
            _songQueue.InsertRange(0, songs);
        }
        else
        {
            _songQueue.AddRange(songs);
        }
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

    public void Clear()
    {
        _songQueue.Clear();
    }
    
    public bool IsEmpty() => _songQueue.Count == 0;
}
