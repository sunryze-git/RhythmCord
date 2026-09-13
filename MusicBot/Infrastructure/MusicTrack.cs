using YoutubeExplode.Channels;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;

namespace MusicBot.Infrastructure;

public class MusicTrack : IVideo, IAsyncDisposable
{
    public MusicTrack(IVideo video, string query, SongSource source) // conversion of IVideo to CustomSong
    {
        Query = query;
        Url = video.Url;
        Title = video.Title;
        Author = video.Author;
        Duration = video.Duration;
        Thumbnails = video.Thumbnails;
        Id = video.Id;
        Source = source;
        ResolvedVideo = video;
    }

    public MusicTrack(string query, string url, string title, string author, TimeSpan duration, string thumbnail,
        SongSource source, IVideo? explodeInternalVideo = null)
    {
        Query = query;
        Url = url;
        Title = title;
        Duration = duration;
        Source = source;
        ResolvedVideo = explodeInternalVideo;

        // Construct Author
        Author = new Author(new ChannelId(), author);

        // Construct Thumbnails
        Thumbnails = new List<Thumbnail>
        {
            new(thumbnail, new Resolution())
        }.AsReadOnly();
    }

    /// <summary>
    ///     Represents the initial query used to find this song. It can be a search term, or the original URL.
    /// </summary>
    public string Query { get; }

    public SongSource Source { get; }

    // YouTubeExplode queries can have their video object stored here for convenience, if available.
    public IVideo? ResolvedVideo { get; set; } // default null, speciifed if available.

    public Task<Stream>? PreResolvedStreamTask { get; set; }
    public Stream? PreResolvedStream { get; set; }
    public bool IsPreResolving => PreResolvedStreamTask != null;
    public bool IsPreResolved => PreResolvedStream != null || PreResolvedStreamTask != null;
    public VideoId Id { get; }
    public string Url { get; }
    public string Title { get; }
    public Author Author { get; }
    public TimeSpan? Duration { get; }
    public IReadOnlyList<Thumbnail> Thumbnails { get; }

    public async ValueTask DisposeAsync()
    {
        if (PreResolvedStreamTask != null)
        {
            try
            {
                var stream = await PreResolvedStreamTask;
                await stream.DisposeAsync();
            }
            catch
            {
            }
            PreResolvedStreamTask = null;
        }

        if (PreResolvedStream != null)
        {
            await PreResolvedStream.DisposeAsync();
            PreResolvedStream = null;
        }

        GC.SuppressFinalize(this);
    }
}

public enum SongSource
{
    Cobalt,
    Direct,
    SoundCloud,
    YouTube,
    Ytdlp
}
