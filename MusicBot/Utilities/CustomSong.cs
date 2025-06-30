using YoutubeExplode.Channels;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;

namespace MusicBot.Utilities;

public class CustomSong : IVideo
{
    public CustomSong(IVideo video, string query, SongSource source) // conversion of IVideo to CustomSong
    {
        Query = query;
        Url = video.Url;
        Title = video.Title;
        Author = video.Author;
        Duration = video.Duration;
        Thumbnails = video.Thumbnails;
        Id = video.Id;
        Source = source;
    }

    public CustomSong(string query, string url, string title, string author, TimeSpan duration, string thumbnail, SongSource source)
    {
        Query = query;
        Url = url;
        Title = title;
        Duration = duration;
        Source = source;
        
        // Construct Author
        Author = new Author(new ChannelId(), author);

        // Construct Thumbnails
        Thumbnails = new List<Thumbnail>
        {
            new(thumbnail, new Resolution())
        }.AsReadOnly();
    }
    
    /// <summary>
    /// Represents the initial query used to find this song. It can be a search term, or the original URL.
    /// </summary>
    public string Query { get; }
    public VideoId Id { get; }
    public string Url { get; }
    public string Title { get; }
    public Author Author { get; }
    public TimeSpan? Duration { get; }
    public IReadOnlyList<Thumbnail> Thumbnails { get; }
    public SongSource Source { get; }
}

public enum SongSource
{
    Cobalt,
    Direct,
    SoundCloud,
    YouTube,
    Ytdlp
}