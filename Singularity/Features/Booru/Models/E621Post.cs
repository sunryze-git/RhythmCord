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

    /// <summary>
    /// Prefers the compressed JPG sample for fast rendering in Discord embeds, then the full file if it is an image.
    /// Videos (webm/mp4/swf) cannot render as an embed image, so they fall back to their preview thumbnail.
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

    /// <summary>
    /// Formats artists into a single string for embed footers.
    /// </summary>
    public string ArtistName => Tags?.Artist is { Count: > 0 } artists
        ? string.Join(", ", artists)
        : "unknown artist";
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
