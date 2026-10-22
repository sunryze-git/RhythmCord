using System.Text.Json.Serialization;

namespace Singularity.Features.Booru.Models;

// e621 omits or nulls several of these objects (deleted/blacklisted posts, videos), so they are nullable.
public record E621Post(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("rating")] string? Rating,
    [property: JsonPropertyName("fav_count")] int FavCount,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("uploader_name")] string? UploaderName,
    [property: JsonPropertyName("sources")] List<string>? Sources,
    [property: JsonPropertyName("file")] E621File? File,
    [property: JsonPropertyName("sample")] E621Sample? Sample,
    [property: JsonPropertyName("preview")] E621Preview? Preview,
    [property: JsonPropertyName("score")] E621Score? Score,
    [property: JsonPropertyName("tags")] E621Tags? Tags
)
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "webp"
    };

    // Formats Discord can play inline in a media gallery (flash cannot be played anywhere).
    private static readonly HashSet<string> PlayableVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "webm", "mp4"
    };

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("comment_count")]
    public int CommentCount { get; init; }

    /// <summary>
    /// What to show in the message: the video itself when Discord can play it, otherwise the best still image.
    /// </summary>
    public string? GetMediaUrl() =>
        File is { Url.Length: > 0 } && File.Ext is { } ext && PlayableVideoExtensions.Contains(ext)
            ? File.Url
            : GetBestImageUrl();

    /// <summary>
    /// Prefers the compressed JPG sample because it loads fast in Discord, then the full file if it is an image.
    /// Anything else (video/flash) falls back to its preview thumbnail.
    /// </summary>
    public string? GetBestImageUrl()
    {
        if (Sample is { Has: true } && !string.IsNullOrEmpty(Sample.Url)) return Sample.Url;
        if (File is { Url.Length: > 0 } && File.Ext is { } ext && ImageExtensions.Contains(ext)) return File.Url;
        return string.IsNullOrEmpty(Preview?.Url) ? null : Preview.Url;
    }

    /// <summary>
    /// Direct URL to the post on e621.
    /// </summary>
    public string WebUrl => $"https://e621.net/posts/{Id}";

    // e621 files these under the artist category, but they are notices rather than people.
    // todo: maybe these should be displayed in the discord embed rather than filtered out???
    private static readonly HashSet<string> NonArtistTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "conditional_dnp", "avoid_posting", "sound_warning", "epilepsy_warning", "unknown_artist", "anonymous_artist"
    };

    private List<string> Artists => Tags?.Artist?.Where(a => !NonArtistTags.Contains(a)).ToList() ?? [];

    /// <summary>
    /// Formats artists into a single display string.
    /// </summary>
    public string ArtistName => Artists is { Count: > 0 } artists
        ? string.Join(", ", artists.Select(a => a.Replace('_', ' ')))
        : "unknown artist";

    /// <summary>
    /// Search for the (first) artist's other work, or null when the artist is unknown.
    /// </summary>
    public string? ArtistUrl => Artists is [var first, ..]
        ? $"https://e621.net/posts?tags={Uri.EscapeDataString(first)}"
        : null;
}

public record E621File(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("ext")] string? Ext,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("md5")] string? Md5,
    [property: JsonPropertyName("url")] string? Url
);

public record E621Sample(
    [property: JsonPropertyName("has")] bool Has,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("alt")] string? AltUrl
);

public record E621Preview(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("alt")] string? AltUrl
);

public record E621Score(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("up")] int Up,
    [property: JsonPropertyName("down")] int Down
);

public record E621Tags(
    [property: JsonPropertyName("general")] List<string>? General,
    [property: JsonPropertyName("artist")] List<string>? Artist,
    [property: JsonPropertyName("copyright")] List<string>? Copyright,
    [property: JsonPropertyName("character")] List<string>? Character,
    [property: JsonPropertyName("species")] List<string>? Species,
    [property: JsonPropertyName("meta")] List<string>? Meta
);
