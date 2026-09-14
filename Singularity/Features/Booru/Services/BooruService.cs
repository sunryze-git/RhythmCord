using NetCord;
using NetCord.Rest;
using Singularity.Features.Booru.Clients;
using Singularity.Features.Booru.Models;

namespace Singularity.Features.Booru.Services;

public class BooruService(IE621Client client)
{
    internal async Task<EmbedProperties?> GetRandomPostEmbedAsync(
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
                _ => null
            };

            if (typeTags is not null)
                tagList.Add(typeTags);
        }

        tagList.Add("order:random");

        var finalTagQuery = string.Join(" ", tagList);
        var posts = await client.GetPostsAsync(finalTagQuery, limit: 10, cancellationToken);

        var post = posts.FirstOrDefault();
        if (post is null) return null;

        return BuildEmbed(post);
    }

    internal EmbedProperties BuildEmbed(E621Post post)
    {
        var imageUrl = post.GetBestImageUrl();
        var artist = post.ArtistName;
        var sourceUrl = post.Sources?.FirstOrDefault();

        var description = $"""
            **Score**:  `{post.Score.Total} ({post.Score.Up} / {post.Score.Down})`
            **Rating**: `{post.Rating.ToUpper()}`
            **Favs**:   `{post.FavCount}`
            {(string.IsNullOrEmpty(sourceUrl) ? "" : $"**[Source]({sourceUrl})**")}
            """;

        var embed = new EmbedProperties
        {
            Title = $"e621 Post #{post.Id}",
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
