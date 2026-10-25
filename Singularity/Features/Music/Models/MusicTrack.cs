namespace Singularity.Features.Music.Models;

internal enum SongSource
{
    YouTube,
    Ytdlp
}

/// <summary>Provider-neutral description of a track. Any provider-specific state lives in its resolver.</summary>
internal class MusicTrackNew
{
    // Identifies this entry in buttons and menus that outlive the message they were drawn for.
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public required string Query { get; init; }
    public required string Url { get; init; }
    public required string Title { get; init; }
    public required string Author { get; init; }
    public required TimeSpan? Duration { get; init; }
    public required string? ThumbnailUrl { get; init; }
    public required SongSource Source { get; init; }

    // Optional provider-specific ID (e.g. YouTube VideoId string)
    public string? MediaId { get; init; }

    // Discord user id of whoever queued the track (0 = unknown). Used for skip permissions.
    public ulong RequestedBy { get; set; }
}
