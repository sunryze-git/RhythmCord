using Newtonsoft.Json;

namespace MusicBot.Parsers;

public record Song(
    [property: JsonProperty("_type")] string Type,
    [property: JsonProperty("ie_key")] string IeKey,
    [property: JsonProperty("id")] string Id,
    [property: JsonProperty("url")] string Url,
    [property: JsonProperty("title")] string Title,
    [property: JsonProperty("description")] object Description,
    [property: JsonProperty("duration")] float? Duration,
    [property: JsonProperty("channel_id")] string ChannelId,
    [property: JsonProperty("channel")] string Channel,
    [property: JsonProperty("channel_url")] string ChannelUrl,
    [property: JsonProperty("uploader")] string Uploader,
    [property: JsonProperty("uploader_id")] string UploaderId,
    [property: JsonProperty("uploader_url")] string UploaderUrl,
    [property: JsonProperty("thumbnails")] IReadOnlyList<Thumbnail>? Thumbnails,
    [property: JsonProperty("timestamp")] object Timestamp,
    [property: JsonProperty("release_timestamp")] object ReleaseTimestamp,
    [property: JsonProperty("availability")] object Availability,
    [property: JsonProperty("view_count")] int? ViewCount,
    [property: JsonProperty("live_status")] object LiveStatus,
    [property: JsonProperty("channel_is_verified")] object ChannelIsVerified,
    [property: JsonProperty("__x_forwarded_for_ip")] object XForwardedForIp,
    [property: JsonProperty("webpage_url")] string WebpageUrl,
    [property: JsonProperty("original_url")] string OriginalUrl,
    [property: JsonProperty("webpage_url_basename")] string WebpageUrlBasename,
    [property: JsonProperty("webpage_url_domain")] string WebpageUrlDomain,
    [property: JsonProperty("extractor")] string Extractor,
    [property: JsonProperty("extractor_key")] string ExtractorKey,
    [property: JsonProperty("artists")] IReadOnlyList<string>? Artists,
    [property: JsonProperty("playlist_count")] int? PlaylistCount,
    [property: JsonProperty("playlist")] string Playlist,
    [property: JsonProperty("playlist_id")] string PlaylistId,
    [property: JsonProperty("playlist_title")] string PlaylistTitle,
    [property: JsonProperty("playlist_uploader")] string PlaylistUploader,
    [property: JsonProperty("playlist_uploader_id")] string PlaylistUploaderId,
    [property: JsonProperty("playlist_channel")] string? PlaylistChannel,
    [property: JsonProperty("playlist_channel_id")] string? PlaylistChannelId,
    [property: JsonProperty("playlist_webpage_url")] string PlaylistWebpageUrl,
    [property: JsonProperty("n_entries")] int? NEntries,
    [property: JsonProperty("playlist_index")] int? PlaylistIndex,
    [property: JsonProperty("__last_playlist_index")] int? LastPlaylistIndex,
    [property: JsonProperty("playlist_autonumber")] int? PlaylistAutonumber,
    [property: JsonProperty("epoch")] int? Epoch,
    [property: JsonProperty("duration_string")] string DurationString,
    [property: JsonProperty("release_year")] object? ReleaseYear,
    [property: JsonProperty("_version")] Version Version
);

public record Thumbnail(
    [property: JsonProperty("url")] string Url,
    [property: JsonProperty("height")] int? Height,
    [property: JsonProperty("width")] int? Width,
    [property: JsonProperty("preference")] int? Preference
);

public record Version(
    [property: JsonProperty("version")] string Ver,
    [property: JsonProperty("current_git_head")] object CurrentGitHead,
    [property: JsonProperty("release_git_head")] string ReleaseGitHead,
    [property: JsonProperty("repository")] string Repository
);
