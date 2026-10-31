using System.Text.Json.Nodes;
using NetCord;
using NetCord.Services;
using Singularity.Features.Music.Player;

namespace Singularity.Tests;

/// <summary>The slash commands, run through NetCord's own dispatcher on made-up interactions (see <see cref="MusicHarnessBase" />).</summary>
public sealed class MusicCommandTests : MusicHarnessBase
{
    private static (string, ApplicationCommandOptionType, string) Int(string name, int value) =>
        (name, ApplicationCommandOptionType.Integer, value.ToString());

    private static (string, ApplicationCommandOptionType, string) Text(string name, string value) =>
        (name, ApplicationCommandOptionType.String, value);

    private async Task<string> PublicReplyAsync()
    {
        var json = await ResponseAsync();
        Assert.Equal(4, (int)json["type"]!);
        Assert.Equal(32768, (int)json["data"]!["flags"]!);
        return TextIn(json["data"]);
    }

    // ---------------------------------------------------------------- loop

    [Theory]
    [InlineData(0, LoopMode.Off, "Loop is off.")]
    [InlineData(1, LoopMode.Track, "Looping a.")]
    [InlineData(2, LoopMode.Queue, "Looping the whole queue.")]
    public async Task Loop_takes_the_mode_as_a_choice(int choice, LoopMode expected, string reply)
    {
        var player = await PlayAsync("a");

        AssertSucceeded(await RunCommandAsync("loop", [Int("mode", choice)]));

        Assert.Equal(expected, player.Snapshot.Loop);
        Assert.Equal(reply, (await PublicReplyAsync()).Replace(@"\", string.Empty));
    }

    [Fact]
    public async Task Loop_without_a_mode_moves_to_the_next_one()
    {
        var player = await PlayAsync("a");

        AssertSucceeded(await RunCommandAsync("loop"));
        Assert.Equal(LoopMode.Track, player.Snapshot.Loop);

        Responses.Clear();
        AssertSucceeded(await RunCommandAsync("loop"));
        Assert.Equal(LoopMode.Queue, player.Snapshot.Loop);
    }

    [Fact]
    public async Task The_loop_choices_offered_to_Discord_are_the_three_modes_and_optional()
    {
        var commands = await RegisteredCommandsAsync();

        var mode = Assert.Single(commands.Single(c => (string)c!["name"]! == "loop")!["options"]!.AsArray())!;
        Assert.Equal(["Off", "Track", "Queue"], mode["choices"]!.AsArray().Select(c => (string)c!["name"]!));
        Assert.Equal([0, 1, 2], mode["choices"]!.AsArray().Select(c => (int)c!["value"]!));
        Assert.NotEqual(true, mode["required"]?.GetValue<bool>());
    }

    [Fact]
    public async Task Every_command_is_registered_with_a_description_Discord_accepts()
    {
        var commands = await RegisteredCommandsAsync();

        Assert.Equal(
            ["leave", "loop", "play", "queue", "remove", "schedule", "search", "shuffle", "skip", "status", "stop"],
            commands.Select(c => (string)c!["name"]!).Order());
        Assert.All(commands, c => Assert.InRange(((string)c!["description"]!).Length, 1, 100));
    }

    // ---------------------------------------------------------------- remove, skip, shuffle, stop

    [Fact]
    public async Task Remove_takes_the_song_at_that_position()
    {
        var player = await PlayAsync("now", "first", "second");

        AssertSucceeded(await RunCommandAsync("remove", [Int("position", 2)]));

        Assert.Equal(["first"], player.Snapshot.Pending.Select(t => t.Title));
        Assert.Contains("Removed second", await PublicReplyAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public async Task Remove_with_a_position_that_is_not_in_the_queue_says_so_privately(int position)
    {
        var player = await PlayAsync("now", "first", "second");

        AssertSucceeded(await RunCommandAsync("remove", [Int("position", position)]));

        Assert.Contains($"no song at position {position}", await RefusalAsync());
        Assert.Equal(2, player.Snapshot.Pending.Count);
    }

    [Fact]
    public async Task Skip_by_the_person_who_queued_the_track_moves_on()
    {
        var player = await PlayAsync("a", "b");

        AssertSucceeded(await RunCommandAsync("skip"));

        await WaitUntil(() => player.Snapshot.Current?.Title == "b", "the next track");
        Assert.Contains("Skipped", await PublicReplyAsync());
    }

    [Fact]
    public async Task Shuffle_and_stop_answer_in_the_channel()
    {
        var player = await PlayAsync("a", "b", "c");

        AssertSucceeded(await RunCommandAsync("shuffle"));
        Assert.Contains("Queue shuffled", await PublicReplyAsync());

        Responses.Clear();
        AssertSucceeded(await RunCommandAsync("stop"));
        Assert.Contains("Stopped playback", await PublicReplyAsync());
        await WaitUntil(() => player.Snapshot.Current is null, "playback to stop");
    }

    [Fact]
    public async Task Commands_that_need_something_playing_say_so_privately_when_nothing_is()
    {
        Orchestrator.GetOrCreateManager(GuildId);

        AssertSucceeded(await RunCommandAsync("shuffle"));

        Assert.Contains("No song is currently playing", await RefusalAsync());
    }

    // ---------------------------------------------------------------- status and queue

    [Fact]
    public async Task Status_shows_the_now_playing_panel_with_its_controls()
    {
        await PlayAsync("My Song", "next");

        AssertSucceeded(await RunCommandAsync("status"));

        var text = await PublicReplyAsync();
        Assert.Contains("My Song", text);
        var buttons = Flatten(await ResponseAsync()).Where(c => (int)c["type"]! == 2).Select(c => (string)c["custom_id"]!);
        Assert.Contains("music-skip", buttons);
        Assert.Contains("music-loop", buttons);
    }

    [Fact]
    public async Task Queue_opens_at_the_requested_page()
    {
        await PlayAsync(Enumerable.Range(0, 26).Select(i => $"t{i}").ToArray());

        AssertSucceeded(await RunCommandAsync("queue", [Int("page", 3)]));

        Assert.Contains("page 3/3", await PublicReplyAsync());
    }

    // ---------------------------------------------------------------- who may use them

    [Fact]
    public async Task Someone_outside_voice_is_turned_away_before_the_command_runs()
    {
        var player = await PlayAsync("a", "b");

        var result = await RunCommandAsync("skip", userId: OtherUserId);

        var failure = Assert.IsAssignableFrom<IFailResult>(result);
        Assert.Contains("voice channel", failure.Message);
        Assert.Equal("a", player.Snapshot.Current!.Title);
    }

    // ---------------------------------------------------------------- search and play

    [Fact]
    public async Task Search_defers_as_a_components_v2_message_then_posts_the_results_with_a_menu()
    {
        AssertSucceeded(await RunCommandAsync("search", [Text("query", "my words")]));

        var deferral = await ResponseAsync();
        Assert.Equal(5, (int)deferral["type"]!);
        Assert.Equal(32768, (int)deferral["data"]!["flags"]!);

        var edit = Assert.Single(RestCalls);
        Assert.Equal("PATCH", edit.Method);
        Assert.EndsWith("/messages/@original", edit.Path);
        var body = JsonNode.Parse(edit.Body)!;
        Assert.Equal(32768, (int)body["flags"]!);
        Assert.Contains("my words", TextIn(body));
        Assert.Contains(Flatten(body), c => (int)c["type"]! == 3 && ((string)c["custom_id"]!).StartsWith("music-search:"));
    }

    [Fact]
    public async Task Search_menus_opened_by_the_command_belong_to_the_person_who_searched()
    {
        await RunCommandAsync("search", [Text("query", "my words")]);
        var token = ((string)Flatten(JsonNode.Parse(Assert.Single(RestCalls).Body)!).Single(c => (int)c["type"]! == 3)["custom_id"]!)["music-search:".Length..];

        Assert.True(Search.TryPeek(token, out var offer));
        Assert.Equal(UserId, offer.UserId);
        Assert.Equal(["hit0", "hit1"], offer.Results.Select(t => t.Title));
    }

    [Fact]
    public async Task Searching_again_straight_away_is_refused_privately()
    {
        await RunCommandAsync("search", [Text("query", "one")]);
        Responses.Clear();

        AssertSucceeded(await RunCommandAsync("search", [Text("query", "two")]));

        Assert.Contains("Slow down", await RefusalAsync());
    }

    [Fact]
    public async Task A_play_that_fails_replaces_the_deferred_message_with_a_notice_that_is_still_components_v2()
    {
        AssertSucceeded(await RunCommandAsync("play", [Text("query", "nothing matches this")]));

        var deferral = await ResponseAsync();
        Assert.Equal(5, (int)deferral["type"]!);
        Assert.Equal(32768, (int)deferral["data"]!["flags"]!);

        var body = JsonNode.Parse(Assert.Single(RestCalls).Body)!;
        Assert.Equal(32768, (int)body["flags"]!);
        Assert.Null(body["content"]);
        Assert.NotEmpty(TextIn(body));
    }

    // ---------------------------------------------------------------- helpers

    private static List<JsonObject> Flatten(JsonNode node)
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

        Visit(node);
        return found;
    }
}
