using System.Collections.Immutable;
using System.Text.Json.Nodes;
using NetCord;
using NetCord.Rest;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Player;
using Singularity.Features.Music.Views;

namespace Singularity.Tests;

public class MusicViewTests
{
    private const int ContainerType = 17;
    private const int SectionType = 9;
    private const int TextDisplayType = 10;
    private const int ThumbnailType = 11;
    private const int ActionRowType = 1;
    private const int ButtonType = 2;
    private const int MenuType = 3;

    // ---------------------------------------------------------------- helpers

    private static MusicTrackNew Track(string title, string author = "author", string? thumbnail = null, ulong by = 0, bool live = false) => new()
    {
        Query = title,
        Url = "https://example.com/watch?v=" + Uri.EscapeDataString(title),
        Title = title,
        Author = author,
        Duration = live ? null : TimeSpan.FromSeconds(200),
        ThumbnailUrl = thumbnail,
        Source = SongSource.YouTube,
        RequestedBy = by
    };

    private static PlayerSnapshot Snapshot(MusicTrackNew? current, int queued, LoopMode loop = LoopMode.Off, int votes = 0) =>
        new(current, Enumerable.Range(0, queued).Select(i => Track($"queued {i}")).ToImmutableList(), loop, votes, false);

    private static async Task<JsonObject> SerializeAsync(MessageView view)
    {
        var content = view.ToMessage().Serialize();
        return (JsonObject)JsonNode.Parse(await content.ReadAsStringAsync())!;
    }

    // Every JSON object under the message that is a component (has a "type").
    private static List<JsonObject> Components(JsonNode node)
    {
        var found = new List<JsonObject>();
        void Visit(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    if (o["type"] is not null) found.Add(o);
                    foreach (var child in o.Select(p => p.Value)) Visit(child);
                    break;
                case JsonArray a:
                    foreach (var child in a) Visit(child);
                    break;
            }
        }

        Visit(node["components"]);
        return found;
    }

    private static List<JsonObject> OfType(IEnumerable<JsonObject> components, int type) =>
        components.Where(c => (int)c["type"]! == type).ToList();

    private static string TextOf(IEnumerable<JsonObject> components) =>
        string.Concat(OfType(components, TextDisplayType).Select(c => (string)c["content"]!));

    /// <summary>Everything Discord enforces on a Components V2 message that these views could break.</summary>
    private static async Task<JsonObject> AssertDiscordAcceptsAsync(MessageView view)
    {
        var message = await SerializeAsync(view);
        var components = Components(message);

        Assert.Equal(32768, (int)message["flags"]! & 32768);
        Assert.Null(message["content"]);
        Assert.Null(message["embeds"]);
        Assert.Equal(ContainerType, (int)message["components"]![0]!["type"]!);

        Assert.True(components.Count <= 40, $"{components.Count} components, Discord allows 40");
        Assert.True(TextOf(components).Length <= 4000, "text displays are limited to 4000 characters in total");

        var ids = components.Select(c => (string?)c["custom_id"]).Where(id => id is not null).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.InRange(id!.Length, 1, 100));

        foreach (var button in OfType(components, ButtonType))
            Assert.InRange(((string)button["label"]!).Length, 1, 80);

        foreach (var row in OfType(components, ActionRowType))
        {
            var children = row["components"]!.AsArray();
            Assert.True(children.Count is >= 1 and <= 5);
            Assert.True(children.All(c => (int)c!["type"]! == ButtonType) || children.Count == 1 && (int)children[0]!["type"]! == MenuType,
                "a row holds up to 5 buttons or exactly one menu");
        }

        foreach (var menu in OfType(components, MenuType))
        {
            var options = menu["options"]!.AsArray();
            Assert.InRange(options.Count, 1, 25);
            Assert.True((menu["max_values"]?.GetValue<int>() ?? 1) <= options.Count);
            Assert.True(((string)menu["placeholder"]!).Length <= 150);
            Assert.Equal(options.Count, options.Select(o => (string)o!["value"]!).Distinct().Count());

            foreach (var option in options)
            {
                Assert.InRange(((string)option!["label"]!).Length, 1, 100);
                Assert.InRange(((string)option["value"]!).Length, 1, 100);
                Assert.True(((string?)option["description"])?.Length <= 100);
            }
        }

        foreach (var section in OfType(components, SectionType))
        {
            Assert.InRange(section["components"]!.AsArray().Count, 1, 3);
            Assert.NotNull(section["accessory"]);
        }

        return message;
    }

    // ---------------------------------------------------------------- limits

    private static readonly string LongText = new('w', 400);
    private static readonly MusicTrackNew LongTrack = Track(LongText, new string('a', 400), "https://example.com/t.jpg", by: 42);

    private static readonly PlayerSnapshot Crowded = new(
        LongTrack,
        Enumerable.Range(0, 500).Select(i => Track($"{LongText}{i}", new string('a', 400), by: 42)).ToImmutableList(),
        LoopMode.Queue, 7, false);

    private static readonly List<MusicTrackNew> LongResults =
        Enumerable.Range(0, 25).Select(i => Track($"{LongText}{i}", new string('a', 400))).ToList();

    private static readonly Dictionary<string, Func<MessageView>> Views = new()
    {
        ["notice"] = () => MusicViews.Notice("Something happened."),
        ["failure"] = () => MusicViews.Notice(new string('x', 1500), failure: true),
        ["added one"] = () => MusicViews.Added([Track("one", thumbnail: "https://example.com/t.jpg")], playsNext: false),
        ["added next"] = () => MusicViews.Added([Track("one")], playsNext: true),
        ["added playlist"] = () => MusicViews.Added([Track("one"), Track("two"), Track("three")], playsNext: false),
        ["added long"] = () => MusicViews.Added([LongTrack], playsNext: false),
        ["playing"] = () => MusicViews.NowPlaying(Snapshot(Track("song", by: 7), 3), TimeSpan.FromSeconds(65)),
        ["playing live"] = () => MusicViews.NowPlaying(Snapshot(Track("song", live: true), 0), TimeSpan.Zero),
        ["playing long"] = () => MusicViews.NowPlaying(Crowded, TimeSpan.FromHours(2)),
        ["nothing playing"] = () => MusicViews.NowPlaying(PlayerSnapshot.Empty, TimeSpan.Zero),
        ["queue empty"] = () => MusicViews.Queue(PlayerSnapshot.Empty, 1, 10),
        ["queue one page"] = () => MusicViews.Queue(Snapshot(Track("now"), 4), 1, 10),
        ["queue first"] = () => MusicViews.Queue(Crowded, 1, 10),
        ["queue middle"] = () => MusicViews.Queue(Crowded, 20, 10),
        ["queue last"] = () => MusicViews.Queue(Crowded, 50, 10),
        ["queue beyond"] = () => MusicViews.Queue(Crowded, 999, 10),
        ["queue before"] = () => MusicViews.Queue(Crowded, -3, 10),
        ["queue biggest page"] = () => MusicViews.Queue(Crowded, 2, 25),
        ["queue oversized page"] = () => MusicViews.Queue(Crowded, 1, 500),
        ["search"] = () => MusicViews.SearchResults("daft punk", "token", LongResults.Take(5).ToList()),
        ["search full menu"] = () => MusicViews.SearchResults(new string('q', 400), "token", LongResults)
    };

    public static TheoryData<string> ViewNames() => new(Views.Keys);

    [Theory]
    [MemberData(nameof(ViewNames))]
    public async Task Every_view_stays_within_what_Discord_accepts(string name) =>
        await AssertDiscordAcceptsAsync(Views[name]());

    // ---------------------------------------------------------------- now playing

    [Fact]
    public async Task Now_playing_shows_the_track_its_progress_and_who_queued_it()
    {
        var view = MusicViews.NowPlaying(Snapshot(Track("My Song", "The Band", by: 42), 3, votes: 2), TimeSpan.FromSeconds(65));

        var text = TextOf(Components(await SerializeAsync(view)));

        Assert.Contains("[My Song](https://example.com/watch?v=My%20Song)", text);
        Assert.Contains("The Band", text);
        Assert.Contains("1:05 / 3:20", text);
        Assert.Contains("<@42>", text);
        Assert.Contains("3 up next", text);
        Assert.Contains("2 skip vote(s)", text);
    }

    [Fact]
    public async Task Now_playing_has_a_thumbnail_section_only_when_the_track_has_a_picture()
    {
        var with = Components(await SerializeAsync(MusicViews.NowPlaying(Snapshot(Track("a", thumbnail: "https://example.com/t.jpg"), 0), TimeSpan.Zero)));
        var without = Components(await SerializeAsync(MusicViews.NowPlaying(Snapshot(Track("a"), 0), TimeSpan.Zero)));

        Assert.Single(OfType(with, SectionType));
        Assert.Equal("https://example.com/t.jpg", (string)OfType(with, ThumbnailType)[0]["media"]!["url"]!);
        Assert.Empty(OfType(without, SectionType));
    }

    [Theory]
    [InlineData(LoopMode.Off, "Loop: Off")]
    [InlineData(LoopMode.Track, "Loop: Track")]
    [InlineData(LoopMode.Queue, "Loop: Queue")]
    public async Task The_loop_button_names_the_current_mode(LoopMode mode, string label)
    {
        var components = Components(await SerializeAsync(MusicViews.NowPlaying(Snapshot(Track("a"), 0, mode), TimeSpan.Zero)));

        var button = OfType(components, ButtonType).Single(b => (string)b["custom_id"]! == MusicIds.Loop);
        Assert.Equal(label, (string)button["label"]!);
    }

    [Fact]
    public async Task Now_playing_offers_every_control()
    {
        var components = Components(await SerializeAsync(MusicViews.NowPlaying(Snapshot(Track("a"), 0), TimeSpan.Zero)));

        var ids = OfType(components, ButtonType).Select(b => (string)b["custom_id"]!).ToList();

        Assert.Equal([MusicIds.Skip, MusicIds.Loop, MusicIds.Shuffle, MusicIds.Stop, MusicIds.Queue, MusicIds.Refresh], ids);
    }

    [Fact]
    public async Task Now_playing_with_nothing_playing_has_no_controls()
    {
        var components = Components(await SerializeAsync(MusicViews.NowPlaying(PlayerSnapshot.Empty, TimeSpan.Zero)));

        Assert.Empty(OfType(components, ButtonType));
        Assert.Contains("Nothing is playing", TextOf(components));
    }

    [Fact]
    public async Task Track_titles_cannot_inject_formatting_or_links()
    {
        var view = MusicViews.NowPlaying(Snapshot(Track("**bold** [click](https://evil.example) # head"), 0), TimeSpan.Zero);

        var text = TextOf(Components(await SerializeAsync(view)));

        Assert.Contains(@"\*\*bold\*\* \[click\](https://evil.example) \# head", text);
        Assert.DoesNotContain("**bold**", text);
    }

    // ---------------------------------------------------------------- queue

    [Fact]
    public async Task Queue_pages_are_numbered_across_the_whole_queue()
    {
        var snapshot = Snapshot(Track("now"), 25);

        var components = Components(await SerializeAsync(MusicViews.Queue(snapshot, 2, 10)));

        var text = TextOf(components);
        Assert.Contains("`11` **queued 10**", text);
        Assert.Contains("`20` **queued 19**", text);
        Assert.DoesNotContain("queued 20", text);
        Assert.Contains("25 queued · page 2/3", text);
    }

    [Fact]
    public async Task Queue_navigation_is_disabled_at_the_ends()
    {
        var snapshot = Snapshot(Track("now"), 25);

        async Task<string> Buttons(int page)
        {
            var buttons = OfType(Components(await SerializeAsync(MusicViews.Queue(snapshot, page, 10))), ButtonType);
            return string.Join(' ', buttons.Select(b =>
                $"{(string)b["custom_id"]!}{((b["disabled"]?.GetValue<bool>() ?? false) ? " (disabled)" : string.Empty)}"));
        }

        Assert.Equal("music-page:0 (disabled) music-page:1 music-page:2", await Buttons(1));
        Assert.Equal("music-page:1 music-page:2 music-page:3", await Buttons(2));
        Assert.Equal("music-page:2 music-page:3 music-page:4 (disabled)", await Buttons(3));
    }

    [Fact]
    public async Task Queue_page_out_of_range_shows_the_nearest_page()
    {
        var snapshot = Snapshot(Track("now"), 25);

        Assert.Contains("page 3/3", TextOf(Components(await SerializeAsync(MusicViews.Queue(snapshot, 99, 10)))));
        Assert.Contains("page 1/3", TextOf(Components(await SerializeAsync(MusicViews.Queue(snapshot, -5, 10)))));
    }

    [Fact]
    public async Task The_remove_menu_lists_this_pages_tracks_by_id_and_remembers_the_page()
    {
        var snapshot = Snapshot(Track("now"), 25);

        var menu = OfType(Components(await SerializeAsync(MusicViews.Queue(snapshot, 3, 10))), MenuType).Single();

        Assert.Equal("music-remove:3", (string)menu["custom_id"]!);
        Assert.Equal(5, menu["options"]!.AsArray().Count);
        Assert.Equal(5, (int)menu["max_values"]!);
        Assert.Equal(snapshot.Pending.Skip(20).Select(t => t.Id), menu["options"]!.AsArray().Select(o => (string)o!["value"]!));
        Assert.StartsWith("21. ", (string)menu["options"]![0]!["label"]!);
    }

    [Fact]
    public async Task An_empty_queue_has_no_menu_and_says_what_is_playing()
    {
        var components = Components(await SerializeAsync(MusicViews.Queue(Snapshot(Track("Now Song"), 0), 1, 10)));

        Assert.Empty(OfType(components, MenuType));
        Assert.Contains("Queue is empty", TextOf(components));
        Assert.Contains("Now Song", TextOf(components));
    }

    // ---------------------------------------------------------------- search

    [Fact]
    public async Task Search_results_are_listed_and_the_menu_values_are_their_positions()
    {
        var results = new[] { Track("first", "A"), Track("second", "B") };

        var components = Components(await SerializeAsync(MusicViews.SearchResults("my words", "tok", results)));

        var menu = OfType(components, MenuType).Single();
        Assert.Equal("music-search:tok", (string)menu["custom_id"]!);
        Assert.Equal(["0", "1"], menu["options"]!.AsArray().Select(o => (string)o!["value"]!));
        Assert.Contains("my words", TextOf(components));
        Assert.Contains("`2` **second** · B", TextOf(components));
    }

    // ---------------------------------------------------------------- added

    [Fact]
    public async Task Added_says_how_many_more_a_playlist_brought()
    {
        var components = Components(await SerializeAsync(MusicViews.Added([Track("one"), Track("two"), Track("three")], playsNext: false)));

        Assert.Contains("Added playlist to queue", TextOf(components));
        Assert.Contains("…and 2 more", TextOf(components));
    }

    [Fact]
    public async Task Added_says_when_a_song_plays_next()
    {
        var components = Components(await SerializeAsync(MusicViews.Added([Track("one")], playsNext: true)));

        Assert.Contains("Playing next", TextOf(components));
    }

    // ---------------------------------------------------------------- message plumbing

    [Fact]
    public void A_view_is_sent_as_components_v2_and_never_pings_anyone()
    {
        var message = MusicViews.Notice("hi").ToMessage();

        Assert.Equal(MessageFlags.IsComponentsV2, message.Flags);
        Assert.Null(message.Content);
        Assert.Equal(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral, MusicViews.Notice("hi").ToMessage(ephemeral: true).Flags);
        Assert.NotNull(message.AllowedMentions);
    }

    [Fact]
    public async Task Editing_a_message_keeps_it_a_components_v2_message()
    {
        var callback = InteractionCallback.ModifyMessage(MusicViews.Notice("hi").ApplyTo);

        var json = JsonNode.Parse(await callback.Serialize().ReadAsStringAsync())!;

        Assert.Equal(7, (int)json["type"]!);
        Assert.Equal(32768, (int)json["data"]!["flags"]!);
        Assert.Single(json["data"]!["components"]!.AsArray());
    }
}
