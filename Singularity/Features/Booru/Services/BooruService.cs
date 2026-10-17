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

        // Prefer a post that can actually show an image.
        var post = posts.FirstOrDefault(p => p.GetBestImageUrl() is not null) ?? posts.FirstOrDefault();
        if (post is null) return null;

        return BuildEmbed(post);
    }

    internal EmbedProperties BuildEmbed(E621Post post)
    {
        var imageUrl = post.GetBestImageUrl();
        var artist = post.ArtistName;
        var rating = post.Rating ?? "?";
        var sourceUrl = post.Sources?.FirstOrDefault(IsHttpUrl);

        // Parentheses would end the markdown link early.
        var sourceLine = sourceUrl is null
            ? ""
            : $"**[Source]({sourceUrl.Replace("(", "%28").Replace(")", "%29")})**";

        var description = $"""
            **Score**:  `{post.Score?.Total ?? 0} ({post.Score?.Up ?? 0} / {post.Score?.Down ?? 0})`
            **Rating**: `{rating.ToUpper()}`
            **Favs**:   `{post.FavCount}`
            {sourceLine}
            """;

        var embed = new EmbedProperties
        {
            Title = $"e621 Post #{post.Id}",
            Url = post.WebUrl,
            Description = description,
            Color = rating switch
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

    private static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
