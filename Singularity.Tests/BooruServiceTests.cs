using Singularity.Features.Booru.Clients;
using Singularity.Features.Booru.Models;
using Singularity.Features.Booru.Services;

namespace Singularity.Tests;

public class BooruServiceTests
{
    private sealed class FakeClient(List<E621Post>? posts = null) : IE621Client
    {
        public string? LastTags { get; private set; }
        public int LastLimit { get; private set; }

        public Task<List<E621Post>> GetPostsAsync(string tags, int limit = 10, CancellationToken cancellationToken = default)
        {
            LastTags = tags;
            LastLimit = limit;
            return Task.FromResult(posts ?? []);
        }
    }

    internal static E621Post Post(long id, string? sampleUrl = null, string? fileUrl = null, string ext = "jpg", string? previewUrl = null,
        string rating = "s", List<string>? sources = null, List<string>? artists = null) =>
        new(id, rating, 5, null, null, sources,
            new E621File(1, 1, ext, 1, "md5", fileUrl),
            new E621Sample(sampleUrl is not null, 1, 1, sampleUrl, null),
            new E621Preview(1, 1, previewUrl, null),
            new E621Score(3, 4, 1),
            new E621Tags(null, artists ?? ["someone"], null, null, null, null));

    [Fact]
    public async Task Query_ends_with_random_order_and_includes_user_tags_first()
    {
        var client = new FakeClient();

        await new BooruService(client).GetRandomPostEmbedAsync("cat  tail", null, null);

        Assert.Equal("cat tail order:random", client.LastTags);
        Assert.Equal(10, client.LastLimit);
    }

    [Theory]
    [InlineData("rating:explicit cat")]
    [InlineData("-rating:safe cat")]
    [InlineData("~RATING:e cat")]
    public async Task User_supplied_rating_terms_are_stripped_when_a_rating_is_enforced(string userTags)
    {
        var client = new FakeClient();

        await new BooruService(client).GetRandomPostEmbedAsync(userTags, BooruRating.Safe, null);

        Assert.Equal("cat rating:safe order:random", client.LastTags);
    }

    [Fact]
    public async Task Media_type_filters_are_translated()
    {
        var client = new FakeClient();
        var service = new BooruService(client);

        await service.GetRandomPostEmbedAsync("", null, BooruMediaType.Gif);
        Assert.Equal("type:gif order:random", client.LastTags);

        await service.GetRandomPostEmbedAsync("", null, BooruMediaType.Photo);
        Assert.Equal("-animated -type:webm -type:mp4 -type:swf order:random", client.LastTags);
    }

    [Fact]
    public async Task No_posts_gives_null()
    {
        Assert.Null(await new BooruService(new FakeClient()).GetRandomPostEmbedAsync("x", null, null));
    }

    [Fact]
    public async Task Prefers_a_post_that_can_show_an_image()
    {
        var blank = Post(1, ext: "webm");
        var good = Post(2, sampleUrl: "https://img/2.jpg");

        var embed = await new BooruService(new FakeClient([blank, good])).GetRandomPostEmbedAsync("x", null, null);

        Assert.NotNull(embed);
        Assert.Contains("#2", embed!.Title);
        Assert.Equal("https://img/2.jpg", embed.Image!.Url);
    }

    [Fact]
    public void Embed_source_link_is_http_only_and_parentheses_are_escaped()
    {
        var service = new BooruService(new FakeClient());

        var safe = service.BuildEmbed(Post(1, sources: ["javascript:alert(1)", "https://example.com/a_(b)"]));
        Assert.Contains("[Source](https://example.com/a_%28b%29)", safe.Description);

        var none = service.BuildEmbed(Post(2, sources: ["ftp://nope"]));
        Assert.DoesNotContain("Source", none.Description);
    }

    [Fact]
    public void Embed_survives_missing_objects()
    {
        var bare = new E621Post(9, null, 0, null, null, null, null, null, null, null, null);

        var embed = new BooruService(new FakeClient()).BuildEmbed(bare);

        Assert.Contains("#9", embed.Title);
        Assert.Contains("unknown artist", embed.Footer!.Text);
    }
}

public class E621PostTests
{
    [Fact]
    public void Image_url_prefers_sample_then_image_file_then_preview()
    {
        Assert.Equal("s", BooruServiceTests.Post(1, sampleUrl: "s", fileUrl: "f").GetBestImageUrl());
        Assert.Equal("f", BooruServiceTests.Post(1, fileUrl: "f", ext: "png").GetBestImageUrl());
        Assert.Equal("p", BooruServiceTests.Post(1, fileUrl: "f.webm", ext: "webm", previewUrl: "p").GetBestImageUrl());
        Assert.Null(BooruServiceTests.Post(1, ext: "webm").GetBestImageUrl());
    }
}
