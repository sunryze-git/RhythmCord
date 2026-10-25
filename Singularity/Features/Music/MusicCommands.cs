using Singularity.Features.Music.Models;
using Singularity.Features.Music.Permissions;
using Singularity.Features.Music.Player;
using Singularity.Features.Music.Views;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Singularity.Infrastructure;
using Singularity.Observability;
using Microsoft.Extensions.Logging;

namespace Singularity.Features.Music;

public class MusicCommands(
    ILogger<MusicCommands> logger,
    GuildAudioInstanceOrchestrator orchestrator,
    MusicSearch search,
    CommandCooldown cooldown,
    MusicOptions options,
    BotMetrics metrics,
    ErrorReporter errors) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("play", "Play a song by URL, or by a search query.")]
    [RequireBotConnectPermission]
    [RequireSameVoiceChannelIfConnected]
    public async Task PlayAsync(
        [SlashCommandParameter(Name = "query", Description = "URL or Search Query")] string query,
        [SlashCommandParameter(Name = "next", Description = "Insert as next in queue")] bool insertNext = false)
    {
        using var scope = Begin("play");

        if (await RefuseIfOnCooldownAsync("play")) return;

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.IsComponentsV2));

        GuildAudioInstance? manager = null;
        try
        {
            manager = GetManager();
            var songs = await manager.EnqueueAsync(RequestOrigin.From(Context), query, insertNext);
            await TryModifyResponseAsync(MusicViews.Added(songs, insertNext), "play");
        }
        catch (Exception e)
        {
            await CloseUnjoinedSessionAsync(manager);

            // Expected problems (nothing found, join failed, queue full...) show their own message; bugs show a generic one.
            await TryModifyResponseAsync(MusicViews.Notice(errors.Report(e, "play"), failure: true), "play");
        }
    }

    [SlashCommand("search", "Search for a song and pick which one to play.")]
    [RequireBotConnectPermission]
    [RequireSameVoiceChannelIfConnected]
    public async Task SearchAsync(
        [SlashCommandParameter(Name = "query", Description = "What to search for")] string query)
    {
        using var scope = Begin("search");

        if (await RefuseIfOnCooldownAsync("search")) return;

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.IsComponentsV2));

        try
        {
            var offer = await search.FindAsync(Context.User.Id, query);
            await TryModifyResponseAsync(MusicViews.SearchResults(query, offer.Token, offer.Results), "search");
        }
        catch (Exception e)
        {
            await TryModifyResponseAsync(MusicViews.Notice(errors.Report(e, "search"), failure: true), "search");
        }
    }

    [SlashCommand("stop", "Stops song playback, clears queue.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task StopAsync()
    {
        using var scope = Begin("stop");
        await RespondWithAsync(await MusicControls.StopAsync(GetManager().Player, RequestOrigin.From(Context)));
    }

    [SlashCommand("skip", "Skips the current song (majority vote if others are listening).")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task SkipAsync()
    {
        using var scope = Begin("skip");
        await RespondWithAsync(await MusicControls.SkipAsync(GetManager().Player, RequestOrigin.From(Context)));
    }

    [SlashCommand("leave", "Leaves the voice channel.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task LeaveAsync()
    {
        using var scope = Begin("leave");

        if (!RequestOrigin.From(Context).CanModerate)
        {
            await RespondWithAsync(ControlResult.Refuse("You need the Move Members permission (or be alone with me) to do that."));
            return;
        }

        await RespondWithAsync(ControlResult.Ok("Bye! 👋"));

        if (Context.Guild != null)
        {
            await orchestrator.CloseManagerAsync(Context.Guild.Id);
        }
    }

    [SlashCommand("status", "Shows the current song with playback controls.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task StatusAsync()
    {
        using var scope = Begin("status");

        var player = GetManager().Player;
        await RespondAsync(InteractionCallback.Message(MusicViews.NowPlaying(player.Snapshot, player.Position).ToMessage()));
    }

    [SlashCommand("loop", "Sets what repeats: the current song, the whole queue, or nothing.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task LoopAsync(
        [SlashCommandParameter(Name = "mode", Description = "Leave empty to move to the next mode")] LoopMode? mode = null)
    {
        using var scope = Begin("loop");
        await RespondWithAsync(await MusicControls.LoopAsync(GetManager().Player, mode));
    }

    [SlashCommand("shuffle", "Shuffles the current queue.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task ShuffleAsync()
    {
        using var scope = Begin("shuffle");
        await RespondWithAsync(await MusicControls.ShuffleAsync(GetManager().Player));
    }

    [SlashCommand("remove", "Removes a song from the queue.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task RemoveAsync(
        [SlashCommandParameter(Name = "position", Description = "Position shown in /queue")] int position)
    {
        using var scope = Begin("remove");

        var player = GetManager().Player;
        var pending = player.Snapshot.Pending;
        if (position < 1 || position > pending.Count)
        {
            await RespondWithAsync(ControlResult.Refuse($"There is no song at position {position}."));
            return;
        }

        await RespondWithAsync(await MusicControls.RemoveAsync(player, RequestOrigin.From(Context), pending[position - 1]));
    }

    [SlashCommand("queue", "Shows the current queue.")]
    [RequireUserVoice]
    [RequireBotInVoiceChannel]
    [RequireSameVoiceChannel]
    public async Task QueueAsync(
        [SlashCommandParameter(Name = "page", Description = "Page number")] int page = 1)
    {
        using var scope = Begin("queue");

        var view = MusicViews.Queue(GetManager().Player.Snapshot, page, options.QueuePageSize);
        await RespondAsync(InteractionCallback.Message(view.ToMessage()));
    }

    #region Helpers

    // Counts the command (by name only) and tags this call's logs with guild/user context (console only).
    private IDisposable? Begin(string command)
    {
        metrics.Command(command);
        return logger.BeginScope("guild={GuildId} user={UserId}", Context.Guild?.Id, Context.User.Id);
    }

    private GuildAudioInstance GetManager() => orchestrator.GetOrCreateManager(Context.Guild!.Id);

    private async Task<bool> RefuseIfOnCooldownAsync(string command)
    {
        if (cooldown.TryAcquire(command, Context.User.Id, options.PlayCooldown, out var remaining)) return false;

        metrics.CooldownHit();
        await RespondWithAsync(ControlResult.Refuse($"Slow down! Try again in {Math.Ceiling(remaining.TotalSeconds)}s."));
        return true;
    }

    // A session that never managed to join voice would otherwise linger as an empty shell.
    private async Task CloseUnjoinedSessionAsync(GuildAudioInstance? manager)
    {
        if (manager is { Voice.IsJoined: false } && Context.Guild is { } guild)
            await orchestrator.CloseManagerAsync(guild.Id);
    }

    // Refusals are only shown to the person who asked.
    private Task RespondWithAsync(ControlResult result) =>
        RespondAsync(InteractionCallback.Message(MusicViews.Notice(result.Message, failure: !result.Done).ToMessage(ephemeral: !result.Done)));

    private async Task TryModifyResponseAsync(MessageView view, string command)
    {
        try
        {
            await ModifyResponseAsync(view.ApplyTo);
        }
        catch (Exception e)
        {
            errors.Record(e, $"{command}.respond", $"Could not deliver the /{command} result to Discord.");
        }
    }

    #endregion
}
