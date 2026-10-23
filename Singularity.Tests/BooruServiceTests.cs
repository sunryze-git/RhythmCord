using NetCord.Rest;
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

        await new BooruService(client).GetRandomPostAsync("cat  tail", null, null);

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

        await new BooruService(client).GetRandomPostAsync(userTags, BooruRating.Safe, null);

        Assert.Equal("cat rating:safe order:random", client.LastTags);
    }

    [Fact]
    public async Task Media_type_filters_are_translated()
    {
        var client = new FakeClient();
        var service = new BooruService(client);

        await service.GetRandomPostAsync("", null, BooruMediaType.Gif);
        Assert.Equal("type:gif order:random", client.LastTags);

        await service.GetRandomPostAsync("", null, BooruMediaType.Photo);
        Assert.Equal("-animated -type:webm -type:mp4 -type:swf order:random", client.LastTags);

        await service.GetRandomPostAsync("", null, BooruMediaType.Video);
        Assert.Equal("-type:jpg -type:png -type:gif -type:webp -type:swf order:random", client.LastTags);
    }

    [Fact]
    public async Task No_posts_gives_null()
    {
        Assert.Null(await new BooruService(new FakeClient()).GetRandomPostAsync("x", null, null));
    }

    private static string Text(ComponentContainerProperties message) =>
        string.Join("\n", message.Components.OfType<TextDisplayProperties>().Select(t => t.Content));

    private static string? MediaUrl(ComponentContainerProperties message) =>
        message.Components.OfType<MediaGalleryProperties>().SingleOrDefault()?.Items.Single().Media.Url;

    private static List<LinkButtonProperties> Buttons(ComponentContainerProperties message) =>
        message.Components.OfType<ActionRowProperties>().SingleOrDefault()?.Components.Cast<LinkButtonProperties>().ToList() ?? [];

    [Fact]
    public async Task Prefers_a_post_that_can_show_something()
    {
        var blank = Post(1, ext: "swf");
        var good = Post(2, sampleUrl: "https://img/2.jpg");

        var message = await new BooruService(new FakeClient([blank, good])).GetRandomPostAsync("x", null, null);

        Assert.NotNull(message);
        Assert.Contains("(https://e621.net/posts/2)", Text(message!));
        Assert.Equal("https://img/2.jpg", MediaUrl(message!));
    }

    [Fact]
    public void Playable_videos_go_in_the_gallery_and_flash_falls_back_to_its_preview()
    {
        var service = new BooruService(new FakeClient());

        Assert.Equal("https://static/1.webm", MediaUrl(service.BuildMessage(Post(1, fileUrl: "https://static/1.webm", ext: "webm", previewUrl: "p"))));
        Assert.Equal("p", MediaUrl(service.BuildMessage(Post(2, fileUrl: "https://static/2.swf", ext: "swf", previewUrl: "p"))));
        Assert.Null(MediaUrl(service.BuildMessage(Post(3, ext: "swf"))));
    }

    [Fact]
    public void Source_button_is_http_only()
    {
        var service = new BooruService(new FakeClient());

        var safe = Buttons(service.BuildMessage(Post(1, sources: ["javascript:alert(1)", "https://www.example.com/a_(b)"])));
        var source = Assert.Single(safe, b => b.Label!.StartsWith("Source"));
        Assert.Equal("Source · example.com", source.Label);
        Assert.Equal("https://www.example.com/a_(b)", source.Url);

        var none = Buttons(service.BuildMessage(Post(2, sources: ["ftp://nope", "https://x.y/" + new string('a', 600)])));
        Assert.DoesNotContain(none, b => b.Label!.StartsWith("Source"));
    }

    [Fact]
    public void Original_button_links_the_full_file_and_the_row_is_left_out_when_empty()
    {
        var service = new BooruService(new FakeClient());

        var image = Buttons(service.BuildMessage(Post(1, fileUrl: "https://static/1.png", ext: "png")));
        Assert.Equal("https://static/1.png", Assert.Single(image).Url);

        Assert.Empty(service.BuildMessage(Post(2)).Components.OfType<ActionRowProperties>());
    }

    [Fact]
    public void Message_survives_missing_objects()
    {
        var bare = new E621Post(9, null, 0, null, null, null, null, null, null, null, null);

        var text = Text(new BooruService(new FakeClient()).BuildMessage(bare));

        Assert.Contains("## [Post #9](https://e621.net/posts/9)", text);
        Assert.Contains("-# 🎨 unknown artist\n", text);
        Assert.Contains("e621 #9 • Unrated", text);
    }

    [Fact]
    public void Message_is_titled_by_characters_and_credits_real_artists()
    {
        var post = Post(1) with
        {
            Tags = new E621Tags(null, ["conditional_dnp", "some_artist"], ["pokemon"], ["zan_(zantanerz)", "c", "d", "e"], ["red_fox"], null),
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1656000000)
        };

        var text = Text(new BooruService(new FakeClient()).BuildMessage(post));

        Assert.Contains("## [Zan, C, D +1 more](https://e621.net/posts/1)", text);
        Assert.Contains("-# [🎨 some artist](https://e621.net/posts?tags=some_artist)", text);
        Assert.Contains("-# pokemon · red fox", text);
        Assert.Contains("<t:1656000000:D>", text);
    }

    [Fact]
    public void Notice_tags_are_shown_and_flashing_media_is_spoilered()
    {
        var service = new BooruService(new FakeClient());
        var post = Post(1, sampleUrl: "https://img/1.jpg", artists: ["epilepsy_warning", "sound_warning", "unknown_artist", "someone"]);

        var message = service.BuildMessage(post);

        Assert.Contains("-# ⚠️ Flashing lights · 🔊 Loud audio\n", Text(message) + "\n");
        Assert.DoesNotContain("unknown", Text(message));
        Assert.True(message.Components.OfType<MediaGalleryProperties>().Single().Items.Single().Spoiler);

        var calm = service.BuildMessage(Post(2, sampleUrl: "https://img/2.jpg"));
        Assert.False(calm.Components.OfType<MediaGalleryProperties>().Single().Items.Single().Spoiler);
    }

    [Fact]
    public void E621_text_cannot_inject_markdown()
    {
        Assert.Equal(@"> \*\*hi\*\* \[a link\](y) # \` \<@1>", BooruService.Excerpt("**hi** [a link](y) # ` <@1>"));
    }

    [Fact]
    public void Description_excerpt_strips_dtext_and_is_truncated()
    {
        Assert.Equal("> bold and link here", BooruService.Excerpt("[b]bold[/b] and \"link\":https://x.y here"));
        Assert.Null(BooruService.Excerpt("  [i][/i] "));
        Assert.EndsWith("…", BooruService.Excerpt(new string('a', 1000)));
    }

    [Theory]
    [InlineData(999, "999")]
    [InlineData(1234, "1.2K")]
    [InlineData(33170, "33K")]
    [InlineData(2_500_000, "2.5M")]
    public void Counts_are_compacted(int value, string expected) => Assert.Equal(expected, BooruService.Compact(value));
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
