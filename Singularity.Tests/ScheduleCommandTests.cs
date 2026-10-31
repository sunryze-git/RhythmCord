using System.Text.Json.Nodes;
using NetCord;
using NetCord.Gateway;
using NetCord.Services;

namespace Singularity.Tests;

/// <summary>/schedule, run through NetCord's own dispatcher (see <see cref="MusicHarnessBase" />).</summary>
public sealed class ScheduleCommandTests : MusicHarnessBase
{
    private const ulong OtherVoiceChannelId = 31;

    private static (string, ApplicationCommandOptionType, string) Text(string name, string value) =>
        (name, ApplicationCommandOptionType.String, value);

    private static (string, ApplicationCommandOptionType, string) Int(string name, int value) =>
        (name, ApplicationCommandOptionType.Integer, value.ToString());

    private Task<IExecutionResult> ScheduleAsync(string time, string query = "song", ulong userId = UserId, Guild? guild = null) =>
        RunSubcommandAsync("schedule", "play", [Text("time", time), Text("query", query)], userId, guild: guild);

    private async Task<JsonNode> EditedReplyAsync()
    {
        var edit = Assert.Single(RestCalls, c => c.Method == "PATCH");
        Assert.EndsWith("/messages/@original", edit.Path);
        return JsonNode.Parse(edit.Body)!;
    }

    // Lets the play's time come and waits until the scheduler has dealt with it.
    private async Task ReachScheduledTimeAsync(TimeSpan delay)
    {
        Time.Advance(delay);
        await WaitUntil(() => Scheduler.Pending(GuildId).Count == 0, "the scheduled play to start");
        await Task.Delay(200);
    }

    // ---------------------------------------------------------------- scheduling

    [Fact]
    public async Task Scheduling_confirms_the_time_the_song_and_the_voice_channel()
    {
        AssertSucceeded(await ScheduleAsync("30m"));

        var reply = await EditedReplyAsync();
        var text = TextIn(reply);
        Assert.Equal(32768, (int)reply["flags"]!);
        Assert.Contains($"<t:{Time.GetUtcNow().AddMinutes(30).ToUnixTimeSeconds()}:t>", text);
        Assert.Contains("song", text);
        Assert.Contains($"<#{VoiceChannelId}>", text);

        var play = Assert.Single(Scheduler.Pending(GuildId));
        Assert.Equal(VoiceChannelId, play.VoiceChannelId);
        Assert.Equal(UserId, play.Origin.UserId);
    }

    [Theory]
    [InlineData("whenever", "couldn't read that time")]
    [InlineData("25h", "within the next 24 hours")]
    [InlineData("0m", "within the next 24 hours")]
    public async Task A_time_that_cannot_be_used_is_refused_privately(string time, string reason)
    {
        AssertSucceeded(await ScheduleAsync(time));

        Assert.Contains(reason, await RefusalAsync());
        Assert.Empty(Scheduler.Pending(GuildId));
    }

    [Fact]
    public async Task A_query_that_finds_nothing_is_refused_when_scheduling()
    {
        AssertSucceeded(await ScheduleAsync("30m", "nothing here"));

        Assert.Contains("No songs found", TextIn(await EditedReplyAsync()));
        Assert.Empty(Scheduler.Pending(GuildId));
    }

    [Fact]
    public async Task Scheduling_needs_the_requester_in_a_voice_channel()
    {
        var result = await ScheduleAsync("30m", guild: BuildGuild(listeners: [], botConnected: false));

        Assert.Contains("voice channel", Assert.IsAssignableFrom<IFailResult>(result).Message);
        Assert.Empty(Scheduler.Pending(GuildId));
    }

    [Fact]
    public async Task A_server_can_only_hold_so_many_scheduled_plays()
    {
        var users = Enumerable.Range(100, 11).Select(i => (ulong)i).ToArray();
        var guild = BuildGuild(listeners: users, botConnected: false);

        foreach (var user in users) await ScheduleAsync("30m", userId: user, guild: guild);

        Assert.Equal(10, Scheduler.Pending(GuildId).Count);
        Assert.Contains("already has 10 scheduled plays", TextIn(JsonNode.Parse(RestCalls.Last(c => c.Method == "PATCH").Body)));
    }

    // ---------------------------------------------------------------- when the time comes

    [Fact]
    public async Task Nobody_in_the_voice_channel_drops_the_play_without_a_word()
    {
        await ScheduleAsync("30m");
        CacheGuild(BuildGuild(listeners: [], botConnected: false));

        await ReachScheduledTimeAsync(TimeSpan.FromMinutes(30));

        Assert.Empty(ChannelMessages());
        Assert.False(Orchestrator.GuildIsActive(GuildId));
    }

    [Fact]
    public async Task Someone_in_the_voice_channel_makes_the_bot_join_it_without_the_requester()
    {
        await ScheduleAsync("30m");
        CacheGuild(BuildGuild(listeners: [OtherUserId], botConnected: false));

        await ReachScheduledTimeAsync(TimeSpan.FromMinutes(30));

        // Tests cannot connect to voice, so the attempt to join the saved channel ends in this message.
        var message = Assert.Single(ChannelMessages());
        Assert.Contains("I couldn't join your voice channel", TextIn(message));
        Assert.False(Orchestrator.GuildIsActive(GuildId));
    }

    [Fact]
    public async Task A_bot_busy_in_another_voice_channel_says_so_in_the_text_channel()
    {
        await ScheduleAsync("30m");
        CacheGuild(BuildGuild(listeners: [OtherUserId], botChannelId: OtherVoiceChannelId));

        await ReachScheduledTimeAsync(TimeSpan.FromMinutes(30));

        Assert.Contains("another voice channel", TextIn(Assert.Single(ChannelMessages())));
    }

    [Fact]
    public async Task Nothing_happens_before_the_time()
    {
        await ScheduleAsync("30m");
        CacheGuild(BuildGuild(listeners: [OtherUserId], botConnected: false));

        Time.Advance(TimeSpan.FromMinutes(29));
        await Task.Delay(200);

        Assert.Single(Scheduler.Pending(GuildId));
        Assert.Empty(ChannelMessages());
    }

    // ---------------------------------------------------------------- list and cancel

    [Fact]
    public async Task List_shows_the_plays_waiting_in_this_server()
    {
        await ScheduleAsync("30m", "first song");
        Responses.Clear();

        AssertSucceeded(await RunSubcommandAsync("schedule", "list"));

        var json = await ResponseAsync();
        Assert.Equal(32768, (int)json["data"]!["flags"]!);
        Assert.Contains("first song", TextIn(json["data"]));
    }

    [Fact]
    public async Task List_says_when_nothing_is_scheduled()
    {
        AssertSucceeded(await RunSubcommandAsync("schedule", "list"));

        Assert.Contains("Nothing is scheduled", TextIn((await ResponseAsync())["data"]));
    }

    [Fact]
    public async Task The_requester_can_cancel_and_the_play_never_starts()
    {
        await ScheduleAsync("30m");
        CacheGuild(BuildGuild(listeners: [OtherUserId], botConnected: false));
        var id = Assert.Single(Scheduler.Pending(GuildId)).Id;
        Responses.Clear();

        AssertSucceeded(await RunSubcommandAsync("schedule", "cancel", [Int("id", id)]));

        Assert.Contains($"Cancelled scheduled play {id}", TextIn((await ResponseAsync())["data"]));
        Time.Advance(TimeSpan.FromMinutes(30));
        await Task.Delay(200);
        Assert.Empty(ChannelMessages());
    }

    [Fact]
    public async Task Someone_else_needs_Move_Members_to_cancel()
    {
        await ScheduleAsync("30m");
        var id = Assert.Single(Scheduler.Pending(GuildId)).Id;
        Responses.Clear();

        AssertSucceeded(await RunSubcommandAsync("schedule", "cancel", [Int("id", id)], userId: OtherUserId));
        Assert.Contains("Only the person who scheduled it", await RefusalAsync());
        Assert.Single(Scheduler.Pending(GuildId));

        Responses.Clear();
        AssertSucceeded(await RunSubcommandAsync("schedule", "cancel", [Int("id", id)], userId: OtherUserId, permissions: Permissions.MoveUsers));
        Assert.Empty(Scheduler.Pending(GuildId));
    }

    [Fact]
    public async Task Cancelling_a_number_that_is_not_scheduled_is_refused_privately()
    {
        AssertSucceeded(await RunSubcommandAsync("schedule", "cancel", [Int("id", 42)]));

        Assert.Contains("no scheduled play 42", await RefusalAsync());
    }

    [Fact]
    public async Task Schedule_is_registered_as_play_list_and_cancel()
    {
        var commands = await RegisteredCommandsAsync();

        var schedule = commands.Single(c => (string)c!["name"]! == "schedule")!;
        Assert.Equal(["cancel", "list", "play"], schedule["options"]!.AsArray().Select(o => (string)o!["name"]!).Order());
        Assert.All(schedule["options"]!.AsArray(), o => Assert.InRange(((string)o!["description"]!).Length, 1, 100));
    }
}
