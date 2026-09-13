using System.Diagnostics.CodeAnalysis;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace Singularity.Features.Music.Models;

public enum SongSource
{
    Cobalt,
    Direct,
    SoundCloud,
    YouTube,
    Ytdlp
}

public class MusicTrackNew
{
    public required string Query { get; init; }
    public required string Url { get; init; }
    public required string Title { get; init; }
    public required string Author { get; init; }
    public required TimeSpan? Duration { get; init; }
    public required string? ThumbnailUrl { get; init; }
    public required SongSource Source { get; init; }

    // Optional provider-specific ID (e.g. YouTube VideoId string)
    public string? MediaId { get; init; }

    // Deferred background stream resolution (pre-fetches IStreamInfo without opening sockets)
    public Task<IStreamInfo>? PreResolvedStreamInfoTask { get; set; }

    [MemberNotNullWhen(true, nameof(PreResolvedStreamInfoTask))]
    public bool IsPreResolving => PreResolvedStreamInfoTask is { IsCompleted: false };

    [MemberNotNullWhen(true, nameof(PreResolvedStreamInfoTask))]
    public bool IsPreResolved => PreResolvedStreamInfoTask is { IsCompletedSuccessfully: true };
}

public static class MusicTrackExtensions
{
    public static MusicTrackNew FromYouTubeVideo(this IVideo video, string query)
    {
        return new MusicTrackNew
        {
            Query = query,
            Url = video.Url,
            Title = video.Title,
            Author = video.Author.ChannelTitle,
            Duration = video.Duration,
            ThumbnailUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url,
            Source = SongSource.YouTube,
            MediaId = video.Id.Value
        };
    }

    public static MusicTrackNew FromYouTubeVideo(this PlaylistVideo video, string query)
    {
        return new MusicTrackNew
        {
            Query = query,
            Url = video.Url,
            Title = video.Title,
            Author = video.Author.ChannelTitle,
            Duration = video.Duration,
            ThumbnailUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url,
            Source = SongSource.YouTube,
            MediaId = video.Id.Value
        };
    }
}
