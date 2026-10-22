using System.Globalization;
using System.Text.RegularExpressions;
using NetCord;
using NetCord.Rest;
using Singularity.Features.Booru.Clients;
using Singularity.Features.Booru.Models;

namespace Singularity.Features.Booru.Services;

public partial class BooruService(IE621Client client)
{
    internal async Task<ComponentContainerProperties?> GetRandomPostAsync(
        string userTags,
        BooruRating? ratingFilter,
        BooruMediaType? typeFilter,
        CancellationToken cancellationToken = default)
    {
        var tagList = new List<string>();

        if (!string.IsNullOrWhiteSpace(userTags))
        {
            var tokens = userTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // When a rating is enforced, the user must not be able to smuggle in their own rating:/-rating:/~rating:
            // terms to widen or override it.
            if (ratingFilter.HasValue)
                tokens = tokens.Where(t => !t.Contains("rating:", StringComparison.OrdinalIgnoreCase)).ToArray();

            tagList.AddRange(tokens);
        }

        if (ratingFilter.HasValue)
        {
            var ratingTag = ratingFilter.Value switch
            {
                BooruRating.Safe => "rating:safe",
                BooruRating.Questionable => "rating:questionable",
                BooruRating.Explicit => "rating:explicit",
                _ => "rating:safe"
            };
            tagList.Add(ratingTag);
        }

        if (typeFilter.HasValue)
        {
            var typeTags = typeFilter.Value switch
            {
                BooruMediaType.Photo => "-animated -type:webm -type:mp4 -type:swf",
                BooruMediaType.Gif => "type:gif",
                _ => null
            };

            if (typeTags is not null)
                tagList.Add(typeTags);
        }

        tagList.Add("order:random");

        var finalTagQuery = string.Join(" ", tagList);
        var posts = await client.GetPostsAsync(finalTagQuery, limit: 10, cancellationToken);

        // Prefer a post that can actually show something.
        var post = posts.FirstOrDefault(p => p.GetMediaUrl() is not null) ?? posts.FirstOrDefault();
        if (post is null) return null;

        return BuildMessage(post);
    }

    private const int DescriptionExcerptLength = 280;
    private const int MaxTitleCharacters = 3;
    private const int MaxContextTags = 5;

    // Discord rejects link buttons with longer URLs.
    private const int MaxButtonUrlLength = 512;

    /// <summary>
    /// Lays the post out as a Components V2 container. Unlike an embed, its media gallery can play videos inline.
    /// The message it goes in must carry <see cref="MessageFlags.IsComponentsV2"/> and no content or embeds.
    /// </summary>
    internal ComponentContainerProperties BuildMessage(E621Post post)
    {
        var rating = post.Rating ?? "?";
        var title = Title(post);
        var components = new List<IComponentContainerComponentProperties>();

        var header = new List<string>();
        var artist = Escape($"🎨 {post.ArtistName}");
        header.Add(post.ArtistUrl is { } artistUrl ? $"-# [{artist}]({artistUrl})" : $"-# {artist}");
        header.Add($"## [{Escape(title)}]({post.WebUrl})");
        if (Excerpt(post.Description) is { } excerpt)
            header.Add(excerpt);
        components.Add(new TextDisplayProperties(string.Join("\n", header)));

        if (post.GetMediaUrl() is { } mediaUrl)
        {
            components.Add(new MediaGalleryProperties(
            [
                new MediaGalleryItemProperties(new ComponentMediaProperties(mediaUrl)) { Description = title }
            ]));
        }

        var details = new List<string>();
        var stats = $"**{Compact(post.Score?.Total ?? 0)}** score · **{Compact(post.FavCount)}** favorites";
        if (post.CommentCount > 0)
            stats += $" · **{Compact(post.CommentCount)}** comments";
        details.Add(stats);

        var context = (post.Tags?.Copyright ?? [])
            .Concat(post.Tags?.Species ?? [])
            .Take(MaxContextTags)
            .Select(t => Escape(PrettyTag(t)))
            .ToList();
        if (context.Count > 0)
            details.Add($"-# {string.Join(" · ", context)}");
        components.Add(new TextDisplayProperties(string.Join("\n", details)));

        components.Add(new ComponentSeparatorProperties { Divider = true, Spacing = ComponentSeparatorSpacingSize.Small });

        var footer = new List<string> { $"e621 #{post.Id}", RatingName(rating) };
        if (post.File is { Width: > 0, Height: > 0 } file)
        {
            var size = $"{file.Width}×{file.Height}";
            if (file.Ext is { } ext) size += $" {ext.ToUpperInvariant()}";
            if (file.Size > 0) size += $" · {FileSize(file.Size)}";
            footer.Add(size);
        }
        // Rendered in each viewer's own timezone and locale.
        if (post.CreatedAt is { } created)
            footer.Add($"<t:{created.ToUnixTimeSeconds()}:D>");
        components.Add(new TextDisplayProperties($"-# {string.Join(" • ", footer)}"));

        var buttons = BuildButtons(post);
        if (buttons.Count > 0)
            components.Add(new ActionRowProperties(buttons));

        return new ComponentContainerProperties(components)
        {
            AccentColor = rating switch
            {
                "s" => new Color(0x3B, 0xB2, 0x73),
                "q" => new Color(0xF0, 0xB2, 0x32),
                "e" => new Color(0xE0, 0x45, 0x45),
                _ => new Color(0x80, 0x84, 0x8E)
            }
        };
    }

    internal static List<LinkButtonProperties> BuildButtons(E621Post post)
    {
        var buttons = new List<LinkButtonProperties>();

        // The gallery shows a downscaled sample (or a still, for flash); this opens the real thing.
        if (post.File?.Url is { } fileUrl && IsButtonUrl(fileUrl))
            buttons.Add(new LinkButtonProperties(fileUrl, "Original", EmojiProperties.Standard("🔍")));

        if (post.Sources?.FirstOrDefault(IsButtonUrl) is { } sourceUrl)
        {
            var host = new Uri(sourceUrl).Host;
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
            buttons.Add(new LinkButtonProperties(sourceUrl, $"Source · {host}", EmojiProperties.Standard("🔗")));
        }

        return buttons;
    }

    /// <summary>
    /// Names the post after who is in it, falling back to what it is from, then to its id.
    /// </summary>
    private static string Title(E621Post post)
    {
        var characters = post.Tags?.Character ?? [];
        if (characters.Count > 0)
        {
            var shown = string.Join(", ", characters.Take(MaxTitleCharacters).Select(CharacterName));
            return characters.Count > MaxTitleCharacters ? $"{shown} +{characters.Count - MaxTitleCharacters} more" : shown;
        }

        return post.Tags?.Copyright is [var copyright, ..] ? PrettyTag(copyright) : $"Post #{post.Id}";
    }

    /// <summary>
    /// "zan_(zantanerz)" → "Zan". The parenthetical only exists to tell same-named characters apart on e621.
    /// </summary>
    internal static string CharacterName(string tag)
    {
        var name = DisambiguationSuffix().Replace(tag, "");
        if (name.Length == 0) name = tag;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(PrettyTag(name));
    }

    private static string RatingName(string rating) => rating switch
    {
        "s" => "Safe",
        "q" => "Questionable",
        "e" => "Explicit",
        _ => "Unrated"
    };

    internal static string FileSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => (bytes / (1024d * 1024)).ToString("0.# MB", CultureInfo.InvariantCulture),
        >= 1024 => (bytes / 1024d).ToString("0 KB", CultureInfo.InvariantCulture),
        _ => $"{bytes} B"
    };

    internal static string PrettyTag(string tag) => tag.Replace('_', ' ');

    /// <summary>
    /// Makes e621-supplied text render literally rather than as Discord markdown.
    /// </summary>
    internal static string Escape(string text) => MarkdownChar().Replace(text, @"\$0");

    internal static string Compact(int value) => value switch
    {
        >= 1_000_000 => (value / 1_000_000d).ToString("0.#M", CultureInfo.InvariantCulture),
        >= 10_000 => (value / 1_000d).ToString("0K", CultureInfo.InvariantCulture),
        >= 1_000 => (value / 1_000d).ToString("0.#K", CultureInfo.InvariantCulture),
        _ => value.ToString(CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// Turns the post's DText description into a short plain-text teaser, or null when there is nothing worth showing.
    /// </summary>
    internal static string? Excerpt(string? dtext)
    {
        if (string.IsNullOrWhiteSpace(dtext)) return null;

        var text = DTextLink().Replace(dtext, "$1");
        text = DTextTag().Replace(text, "");
        text = Whitespace().Replace(text, " ").Trim();
        if (text.Length == 0) return null;

        if (text.Length > DescriptionExcerptLength)
            text = text[..DescriptionExcerptLength].TrimEnd() + "…";

        // Quote it so it reads as the uploader's words rather than the bot's.
        return $"> {Escape(text)}";
    }

    // "label":https://… links
    [GeneratedRegex("\"([^\"]+)\":\\S+")]
    private static partial Regex DTextLink();

    // [b], [/i], [url=…], [section=…] and friends
    [GeneratedRegex(@"\[/?[a-z]+(=[^\]]*)?\]", RegexOptions.IgnoreCase)]
    private static partial Regex DTextTag();

    // Inline markup only: escaped text never starts a line, so block markup (#, >, -) cannot trigger.
    [GeneratedRegex(@"[\\*_~`|\[\]<]")]
    private static partial Regex MarkdownChar();

    // trailing "_(…)" qualifier
    [GeneratedRegex(@"_\([^)]*\)$")]
    private static partial Regex DisambiguationSuffix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static bool IsButtonUrl(string? url) => url is { Length: <= MaxButtonUrlLength } && IsHttpUrl(url);

    private static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
