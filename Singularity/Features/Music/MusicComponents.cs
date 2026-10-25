using NetCord;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ComponentInteractions;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Permissions;
using Singularity.Features.Music.Player;
using Singularity.Features.Music.Views;
using Singularity.Observability;

namespace Singularity.Features.Music;

internal readonly record struct Control(GuildPlayer Player, RequestOrigin Who);

/// <summary>What buttons and menus have in common: checking who may use them and answering on the message they sit on.</summary>
public abstract class MusicComponentModule<TContext>(
    GuildAudioInstanceOrchestrator orchestrator,
    MusicOptions options,
    BotMetrics metrics) : ComponentInteractionModule<TContext>
    where TContext : IComponentInteractionContext, IGatewayClientContext, IGuildContext, IUserContext, IChannelContext
{
    private protected MusicOptions Options => options;
    private protected GuildAudioInstanceOrchestrator Orchestrator => orchestrator;

    // Only people in the bot's voice channel may use the controls; anyone else is told so privately.
    private protected async Task<Control?> ControlAsync(string action)
    {
        metrics.Command($"{action}.component");

        if (VoiceChecks.SameChannel(Context.Guild, Context.User.Id, Context.Client.Id) is { } problem)
        {
            await RefuseAsync(problem);
            return null;
        }

        if (!orchestrator.GuildIsActive(Context.Guild!.Id))
        {
            await RefuseAsync("The bot is not in a voice channel in this server.");
            return null;
        }

        return new Control(orchestrator.GetOrCreateManager(Context.Guild.Id).Player, RequestOrigin.From(Context));
    }

    private protected Task UpdateAsync(MessageView view) =>
        Context.Interaction.SendResponseAsync(InteractionCallback.ModifyMessage(view.ApplyTo));

    private protected Task RefuseAsync(string text) =>
        Context.Interaction.SendResponseAsync(InteractionCallback.Message(MusicViews.Notice(text, failure: true).ToMessage(ephemeral: true)));

    // Skipping and stopping hand over to the next track a moment later; wait for that so the panel shows the new one.
    private protected static async Task SettleAsync(GuildPlayer player, MusicTrackNew? ended)
    {
        for (var i = 0; i < 20 && ended is not null && ReferenceEquals(player.Snapshot.Current, ended); i++)
            await Task.Delay(50);
    }

    private protected Task ShowNowPlayingAsync(GuildPlayer player) =>
        UpdateAsync(MusicViews.NowPlaying(player.Snapshot, player.Position));
}

public class MusicButtons(GuildAudioInstanceOrchestrator orchestrator, MusicOptions options, BotMetrics metrics)
    : MusicComponentModule<ButtonInteractionContext>(orchestrator, options, metrics)
{
    [ComponentInteraction(MusicIds.Skip)]
    public async Task SkipAsync()
    {
        if (await ControlAsync("skip") is not { } control) return;

        var playing = control.Player.Snapshot.Current;
        var result = await MusicControls.SkipAsync(control.Player, control.Who);
        if (!result.Done)
        {
            await RefuseAsync(result.Message);
            return;
        }

        if (result.TrackEnded) await SettleAsync(control.Player, playing);
        await ShowNowPlayingAsync(control.Player);
    }

    [ComponentInteraction(MusicIds.Loop)]
    public async Task LoopAsync()
    {
        if (await ControlAsync("loop") is not { } control) return;

        var result = await MusicControls.LoopAsync(control.Player, mode: null);
        if (result.Done) await ShowNowPlayingAsync(control.Player);
        else await RefuseAsync(result.Message);
    }

    [ComponentInteraction(MusicIds.Shuffle)]
    public async Task ShuffleAsync()
    {
        if (await ControlAsync("shuffle") is not { } control) return;

        var result = await MusicControls.ShuffleAsync(control.Player);
        if (result.Done) await ShowNowPlayingAsync(control.Player);
        else await RefuseAsync(result.Message);
    }

    [ComponentInteraction(MusicIds.Stop)]
    public async Task StopAsync()
    {
        if (await ControlAsync("stop") is not { } control) return;

        var result = await MusicControls.StopAsync(control.Player, control.Who);
        if (result.Done) await UpdateAsync(MusicViews.Notice(result.Message));
        else await RefuseAsync(result.Message);
    }

    [ComponentInteraction(MusicIds.Refresh)]
    public async Task RefreshAsync()
    {
        if (await ControlAsync("refresh") is not { } control) return;

        await ShowNowPlayingAsync(control.Player);
    }

    // Opens the queue as a message of its own, so the now playing panel stays where it is.
    [ComponentInteraction(MusicIds.Queue)]
    public async Task QueueAsync()
    {
        if (await ControlAsync("queue") is not { } control) return;

        var view = MusicViews.Queue(control.Player.Snapshot, 1, Options.QueuePageSize);
        await Context.Interaction.SendResponseAsync(InteractionCallback.Message(view.ToMessage(ephemeral: true)));
    }

    [ComponentInteraction(MusicIds.Page)]
    public async Task PageAsync(int page)
    {
        if (await ControlAsync("page") is not { } control) return;

        await UpdateAsync(MusicViews.Queue(control.Player.Snapshot, page, Options.QueuePageSize));
    }
}

public class MusicMenus(
    GuildAudioInstanceOrchestrator orchestrator,
    MusicSearch search,
    MusicOptions options,
    BotMetrics metrics,
    ErrorReporter errors) : MusicComponentModule<StringMenuInteractionContext>(orchestrator, options, metrics)
{
    [ComponentInteraction(MusicIds.Remove)]
    public async Task RemoveAsync(int page)
    {
        if (await ControlAsync("remove") is not { } control) return;

        var chosen = Context.SelectedValues.ToHashSet();
        var removed = 0;
        ControlResult? refusal = null;

        foreach (var song in control.Player.Snapshot.Pending.Where(song => chosen.Contains(song.Id)))
        {
            var result = await MusicControls.RemoveAsync(control.Player, control.Who, song);
            if (result.Done) removed++;
            else refusal ??= result;
        }

        if (removed == 0)
        {
            await RefuseAsync(refusal?.Message ?? "Those songs are no longer in the queue.");
            return;
        }

        await UpdateAsync(MusicViews.Queue(control.Player.Snapshot, page, Options.QueuePageSize));
    }

    [ComponentInteraction(MusicIds.Search)]
    public async Task PickAsync(string token)
    {
        if (!search.TryPeek(token, out var offer))
        {
            await UpdateAsync(MusicViews.Notice("These results have expired. Search again.", failure: true));
            return;
        }

        if (offer.UserId != Context.User.Id)
        {
            await RefuseAsync("Only the person who searched can pick from these results.");
            return;
        }

        var problem = VoiceChecks.SameChannelIfConnected(Context.Guild, Context.User.Id, Context.Client.Id)
                      ?? (Context.Guild!.VoiceStates.GetValueOrDefault(Context.User.Id)?.ChannelId is null
                          ? "Join a voice channel first, then pick again."
                          : null);
        if (problem is not null)
        {
            await RefuseAsync(problem);
            return;
        }

        if (!int.TryParse(Context.SelectedValues.FirstOrDefault(), out var index) || index < 0 || index >= offer.Results.Count
            || !search.TryTake(token, out offer))
        {
            await RefuseAsync("That choice is no longer available.");
            return;
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredModifyMessage);

        GuildAudioInstance? manager = null;
        try
        {
            manager = Orchestrator.GetOrCreateManager(Context.Guild!.Id);
            var songs = await manager.EnqueueAsync(RequestOrigin.From(Context), [offer.Results[index]], next: false);
            await Context.Interaction.ModifyResponseAsync(MusicViews.Added(songs, playsNext: false).ApplyTo);
        }
        catch (Exception e)
        {
            // A session that never managed to join voice would otherwise linger as an empty shell.
            if (manager is { Voice.IsJoined: false })
                await Orchestrator.CloseManagerAsync(Context.Guild!.Id);

            await Context.Interaction.ModifyResponseAsync(MusicViews.Notice(errors.Report(e, "search"), failure: true).ApplyTo);
        }
    }
}
