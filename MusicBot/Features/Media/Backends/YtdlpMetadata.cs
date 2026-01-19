using System.Text.Json.Serialization;

namespace MusicBot.Features.Media.Backends;

public record Song(
    [property: JsonPropertyName("_type")] string Type,
    [property: JsonPropertyName("ie_key")] string IeKey,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")]
    object Description,
    [property: JsonPropertyName("duration")] float? Duration,
    [property: JsonPropertyName("channel_id")] string ChannelId,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("channel_url")]
    string ChannelUrl,
    [property: JsonPropertyName("uploader")] string Uploader,
    [property: JsonPropertyName("uploader_id")]
    string UploaderId,
    [property: JsonPropertyName("uploader_url")]
    string UploaderUrl,
    [property: JsonPropertyName("thumbnails")] IReadOnlyList<Thumbnail>? Thumbnails,
    [property: JsonPropertyName("timestamp")] object Timestamp,
    [property: JsonPropertyName("release_timestamp")]
    object ReleaseTimestamp,
    [property: JsonPropertyName("availability")]
    object Availability,
    [property: JsonPropertyName("view_count")] int? ViewCount,
    [property: JsonPropertyName("live_status")]
    object LiveStatus,
    [property: JsonPropertyName("channel_is_verified")]
    object ChannelIsVerified,
    [property: JsonPropertyName("__x_forwarded_for_ip")]
    object XForwardedForIp,
    [property: JsonPropertyName("webpage_url")]
    string WebpageUrl,
    [property: JsonPropertyName("original_url")]
    string OriginalUrl,
    [property: JsonPropertyName("webpage_url_basename")]
    string WebpageUrlBasename,
    [property: JsonPropertyName("webpage_url_domain")]
    string WebpageUrlDomain,
    [property: JsonPropertyName("extractor")] string Extractor,
    [property: JsonPropertyName("extractor_key")]
    string ExtractorKey,
    [property: JsonPropertyName("artists")] IReadOnlyList<string>? Artists,
    [property: JsonPropertyName("playlist_count")]
    int? PlaylistCount,
    [property: JsonPropertyName("playlist")] string Playlist,
    [property: JsonPropertyName("playlist_id")]
    string PlaylistId,
    [property: JsonPropertyName("playlist_title")]
    string PlaylistTitle,
    [property: JsonPropertyName("playlist_uploader")]
    string PlaylistUploader,
    [property: JsonPropertyName("playlist_uploader_id")]
    string PlaylistUploaderId,
    [property: JsonPropertyName("playlist_channel")]
    string? PlaylistChannel,
    [property: JsonPropertyName("playlist_channel_id")]
    string? PlaylistChannelId,
    [property: JsonPropertyName("playlist_webpage_url")]
    string PlaylistWebpageUrl,
    [property: JsonPropertyName("n_entries")] int? NEntries,
    [property: JsonPropertyName("playlist_index")]
    int? PlaylistIndex,
    [property: JsonPropertyName("__last_playlist_index")]
    int? LastPlaylistIndex,
    [property: JsonPropertyName("playlist_autonumber")]
    int? PlaylistAutonumber,
    [property: JsonPropertyName("epoch")] int? Epoch,
    [property: JsonPropertyName("duration_string")]
    string DurationString,
    [property: JsonPropertyName("release_year")]
    object? ReleaseYear,
    [property: JsonPropertyName("_version")] Version Version
);

public record Thumbnail(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("height")] int? Height,
    [property: JsonPropertyName("width")] int? Width,
    [property: JsonPropertyName("preference")] int? Preference
);

public record Version(
    [property: JsonPropertyName("version")] string Ver,
    [property: JsonPropertyName("current_git_head")]
    object CurrentGitHead,
    [property: JsonPropertyName("release_git_head")]
    string ReleaseGitHead,
    [property: JsonPropertyName("repository")] string Repository
);
