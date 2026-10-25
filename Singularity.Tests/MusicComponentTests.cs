using NetCord;
using NetCord.Services;
using Singularity.Features.Music.Player;
using Singularity.Features.Music.Views;

namespace Singularity.Tests;

/// <summary>
///     Runs NetCord's own dispatcher on made-up button and menu interactions, so the custom IDs, their parameters, the
///     voice checks and the messages that come back are exercised without a Discord connection.
/// </summary>
public sealed class MusicComponentTests : MusicHarnessBase
{
    [Fact]
    public async Task Every_custom_id_the_views_use_reaches_a_handler()
    {
        var player = await PlayAsync("a", "b", "c");

        foreach (var id in new[] { MusicIds.Loop, MusicIds.Shuffle, MusicIds.Refresh, MusicIds.Queue, MusicIds.PageOf(1), MusicIds.PageOf(0) })
            AssertSucceeded(await PressAsync(id));

        Assert.Equal(6, Responses.Count);
        Assert.Equal("a", player.Snapshot.Current!.Title);
    }

    [Fact]
    public async Task An_unknown_custom_id_is_reported_as_not_found()
    {
        var result = await PressAsync("music-nonsense");

        Assert.IsAssignableFrom<IFailResult>(result);
        Assert.Empty(Responses);
    }

    [Fact]
    public async Task A_page_that_is_not_a_number_is_a_failed_parse_not_a_crash()
    {
        await PlayAsync("a");

        var result = await PressAsync("music-page:abc");

        Assert.IsAssignableFrom<IFailResult>(result);
        Assert.IsNotAssignableFrom<IExceptionResult>(result);
    }

    // ---------------------------------------------------------------- controls

    [Fact]
    public async Task The_skip_button_moves_on_and_shows_the_next_track()
    {
        var player = await PlayAsync("first", "second");

        AssertSucceeded(await PressAsync(MusicIds.Skip));

        var json = await ResponseAsync();
        Assert.Equal(7, (int)json["type"]!);
        Assert.Equal("second", player.Snapshot.Current!.Title);
        Assert.Contains("second", TextIn(json["data"]));
        Assert.DoesNotContain("first", TextIn(json["data"]));
    }

    [Fact]
    public async Task The_loop_button_cycles_the_mode_and_the_panel_follows()
    {
        var player = await PlayAsync("a");

        AssertSucceeded(await PressAsync(MusicIds.Loop));
        Assert.Equal(LoopMode.Track, player.Snapshot.Loop);
        Assert.Contains("Loop: Track", TextIn((await ResponseAsync())["data"]));

        Responses.Clear();
        AssertSucceeded(await PressAsync(MusicIds.Loop));
        Assert.Equal(LoopMode.Queue, player.Snapshot.Loop);
        Assert.Contains("Loop: Queue", TextIn((await ResponseAsync())["data"]));
    }

    [Fact]
    public async Task The_stop_button_clears_everything_and_replaces_the_panel_with_a_notice()
    {
        var player = await PlayAsync("a", "b");

        AssertSucceeded(await PressAsync(MusicIds.Stop));

        await WaitUntil(() => player.Snapshot.Current is null, "playback to stop");
        Assert.Empty(player.Snapshot.Pending);
        Assert.Contains("Stopped playback", TextIn((await ResponseAsync())["data"]));
    }

    [Fact]
    public async Task The_queue_button_opens_the_queue_as_a_private_message()
    {
        await PlayAsync("a", "b", "c");

        AssertSucceeded(await PressAsync(MusicIds.Queue));

        var json = await ResponseAsync();
        Assert.Equal(4, (int)json["type"]!);
        Assert.Equal(32768 | 64, (int)json["data"]!["flags"]!);
        Assert.Contains("Up next", TextIn(json["data"]));
        Assert.Contains("**b**", TextIn(json["data"]));
    }

    [Fact]
    public async Task The_page_button_carries_its_page_number_through_to_the_queue()
    {
        await PlayAsync(Enumerable.Range(0, 26).Select(i => $"t{i}").ToArray());

        AssertSucceeded(await PressAsync(MusicIds.PageOf(2)));

        var text = TextIn((await ResponseAsync())["data"]);
        Assert.Contains("page 2/3", text);
        Assert.Contains("`11` **t11**", text);
    }

    [Fact]
    public async Task The_remove_menu_removes_the_chosen_tracks_and_redraws_the_same_page()
    {
        var player = await PlayAsync("now", "keep", "drop1", "drop2");
        var drop = player.Snapshot.Pending.Where(t => t.Title.StartsWith("drop")).Select(t => t.Id).ToArray();

        AssertSucceeded(await ChooseAsync(MusicIds.RemoveFrom(1), drop));

        Assert.Equal(["keep"], player.Snapshot.Pending.Select(t => t.Title));
        var text = TextIn((await ResponseAsync())["data"]);
        Assert.Contains("keep", text);
        Assert.DoesNotContain("drop", text);
    }

    [Fact]
    public async Task Removing_a_track_that_is_already_gone_says_so_privately()
    {
        var player = await PlayAsync("now", "gone");
        var id = player.Snapshot.Pending[0].Id;
        await player.RemoveAsync(player.Snapshot.Pending[0]);

        AssertSucceeded(await ChooseAsync(MusicIds.RemoveFrom(1), [id]));

        Assert.Contains("no longer in the queue", await RefusalAsync());
    }

    // ---------------------------------------------------------------- who may do what

    [Fact]
    public async Task Someone_outside_the_voice_channel_cannot_use_the_controls()
    {
        var player = await PlayAsync("a", "b");

        AssertSucceeded(await PressAsync(MusicIds.Skip, userId: OtherUserId));

        Assert.Contains("You are not in a voice channel", await RefusalAsync());
        Assert.Equal("a", player.Snapshot.Current!.Title);
    }

    [Fact]
    public async Task Controls_on_an_old_panel_after_the_bot_left_say_so()
    {
        AssertSucceeded(await PressAsync(MusicIds.Skip));

        Assert.Contains("not in a voice channel", await RefusalAsync());
    }

    [Fact]
    public async Task A_listener_who_did_not_queue_the_track_casts_a_skip_vote_instead_of_skipping()
    {
        var player = await PlayAsync("a", "b");
        var crowd = BuildGuild(listeners: [UserId, OtherUserId, 22]);

        AssertSucceeded(await PressAsync(MusicIds.Skip, userId: OtherUserId, guild: crowd));

        Assert.Equal("a", player.Snapshot.Current!.Title);
        Assert.Equal(1, player.Snapshot.SkipVotes);
        Assert.Contains("1 skip vote(s)", TextIn((await ResponseAsync())["data"]));
    }

    [Fact]
    public async Task Stop_needs_move_members_when_others_are_listening()
    {
        var player = await PlayAsync("a", "b");
        var crowd = BuildGuild(listeners: [UserId, OtherUserId]);

        AssertSucceeded(await PressAsync(MusicIds.Stop, userId: OtherUserId, guild: crowd));
        Assert.Contains("Move Members", await RefusalAsync());
        Assert.Equal("a", player.Snapshot.Current!.Title);

        Responses.Clear();
        AssertSucceeded(await PressAsync(MusicIds.Stop, userId: OtherUserId, permissions: Permissions.MoveUsers, guild: crowd));
        await WaitUntil(() => player.Snapshot.Current is null, "a moderator to stop playback");
    }

    [Fact]
    public async Task Only_the_person_who_queued_a_track_or_a_moderator_can_remove_it()
    {
        var player = await PlayAsync("now", "mine");
        var crowd = BuildGuild(listeners: [UserId, OtherUserId]);
        var track = player.Snapshot.Pending[0];

        AssertSucceeded(await ChooseAsync(MusicIds.RemoveFrom(1), [track.Id], userId: OtherUserId, guild: crowd));
        Assert.Contains("Only the person who queued it", await RefusalAsync());
        Assert.Single(player.Snapshot.Pending);

        Responses.Clear();
        AssertSucceeded(await ChooseAsync(MusicIds.RemoveFrom(1), [track.Id], userId: OtherUserId, permissions: Permissions.MoveUsers, guild: crowd));
        Assert.Empty(player.Snapshot.Pending);
    }

    // ---------------------------------------------------------------- search menu

    [Fact]
    public async Task A_search_menu_that_has_expired_tells_the_user_to_search_again()
    {
        AssertSucceeded(await ChooseAsync(MusicIds.SearchFor("no-such-token"), ["0"]));

        var json = await ResponseAsync();
        Assert.Equal(7, (int)json["type"]!);
        Assert.Contains("expired", TextIn(json["data"]));
    }

    [Fact]
    public async Task Only_the_person_who_searched_can_pick_from_the_results()
    {
        var offer = await OfferAsync(UserId);

        AssertSucceeded(await ChooseAsync(MusicIds.SearchFor(offer), ["0"], userId: OtherUserId));

        Assert.Contains("Only the person who searched", await RefusalAsync());
        Assert.True(Search.TryPeek(offer, out _), "someone else's click must not use up the results");
    }

    [Fact]
    public async Task Picking_from_outside_the_bots_voice_channel_keeps_the_results_for_another_try()
    {
        var offer = await OfferAsync(OtherUserId);

        AssertSucceeded(await ChooseAsync(MusicIds.SearchFor(offer), ["0"], userId: OtherUserId));

        Assert.Contains("same voice channel", await RefusalAsync());
        Assert.True(Search.TryPeek(offer, out _));
    }

    [Fact]
    public async Task Picking_while_not_in_any_voice_channel_asks_to_join_one_and_keeps_the_results()
    {
        var offer = await OfferAsync(OtherUserId);
        var nobodyConnected = BuildGuild(listeners: [UserId], botConnected: false);

        AssertSucceeded(await ChooseAsync(MusicIds.SearchFor(offer), ["0"], userId: OtherUserId, guild: nobodyConnected));

        Assert.Contains("Join a voice channel", await RefusalAsync());
        Assert.True(Search.TryPeek(offer, out _));
    }

    private async Task<string> OfferAsync(ulong userId) => (await Search.FindAsync(userId, "words")).Token;
}
