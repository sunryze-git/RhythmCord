using NetCord;
using NetCord.Rest;
using Singularity.Features.Booru.Clients;
using Singularity.Features.Booru.Models;

namespace Singularity.Features.Booru.Services;

public class BooruService(IE621Client client)
{
    public async Task<EmbedProperties?> GetRandomPostEmbedAsync(
        string userTags,
        BooruRating? ratingFilter,
        BooruMediaType? typeFilter,
        CancellationToken cancellationToken = default)
    {
        var tagList = new List<string>();

        if (!string.IsNullOrWhiteSpace(userTags))
            tagList.Add(userTags.Trim());

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
                BooruMediaType.Video => "-type:png -type:jpg -type:jpeg -type:gif",
                _ => null
            };

            if (typeTags is not null)
                tagList.Add(typeTags);
        }

        tagList.Add("order:random");

        var finalTagQuery = string.Join(" ", tagList);
        var posts = await client.GetPostsAsync(finalTagQuery, limit: 10, cancellationToken);

        if (typeFilter.HasValue && posts.Count > 0)
        {
            posts = typeFilter.Value switch
            {
                BooruMediaType.Photo => [.. posts.Where(p =>
                    p.File.Ext is "png" or "jpg" or "jpeg" or "webp" &&
                    !p.Tags.General.Contains("animated") &&
                    !p.Tags.Meta.Contains("animated"))],

                BooruMediaType.Gif => [.. posts.Where(p =>
                    p.File.Ext is "gif" ||
                    p.Tags.General.Contains("animated") ||
                    p.Tags.Meta.Contains("animated"))],

                BooruMediaType.Video => [.. posts.Where(p =>
                    p.File.Ext is "webm" or "mp4" or "swf")],

                _ => posts
            };
        }

        var post = posts.FirstOrDefault();
        if (post is null) return null;

        return BuildEmbed(post);
    }

    public EmbedProperties BuildEmbed(E621Post post)
    {
        var isVideo = post.File.Ext is "webm" or "mp4" or "swf";
        var isGif = post.File.Ext is "gif" || post.Tags.General.Contains("animated");

        // Media format badge indicator
        var mediaBadge = isVideo ? "🎥 [VIDEO]" : isGif ? "🎞️ [GIF]" : "🖼️ [IMAGE]";
        var imageUrl = post.GetBestImageUrl();
        var artist = post.ArtistName;
        var sourceUrl = post.Sources?.FirstOrDefault();

        var description = $"""
            **Format**: `{mediaBadge}` (`.{post.File.Ext}`)
            **Rating**: `{post.Rating.ToUpper()}` | **Score**: `{post.Score.Total}` (👍 {post.Score.Up} / 👎 {post.Score.Down}) | **Favs**: `{post.FavCount}`
            **Artist**: {artist}
            {(string.IsNullOrEmpty(sourceUrl) ? "" : $"**[Original Source]({sourceUrl})**")}
            {(isVideo && !string.IsNullOrEmpty(post.File.Url) ? $"\n🎬 **[Direct Video Link]({post.File.Url})**" : "")}
            """;

        var embed = new EmbedProperties
        {
            Title = $"{mediaBadge} e621 Post #{post.Id}",
            Url = post.WebUrl,
            Description = description,
            Color = post.Rating switch
            {
                "s" => new Color(0, 180, 216),
                "q" => new Color(255, 183, 3),
                "e" => new Color(208, 0, 0),
                _ => new Color(128, 128, 128)
            },
            Footer = new EmbedFooterProperties { Text = $"Artist: {artist}" }
        };

        if (!string.IsNullOrEmpty(imageUrl))
        {
            embed.Image = new EmbedImageProperties(imageUrl);
        }

        return embed;
    }
}
