using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Permissions;
using Singularity.Features.Music.Views;
using Singularity.Infrastructure;
using Singularity.Observability;

namespace Singularity.Features.Music;

[SlashCommand("schedule", "Plays something later in a voice channel.")]
public class ScheduleCommands(
    ILogger<ScheduleCommands> logger,
    MusicScheduler scheduler,
    TimeProvider time,
    CommandCooldown cooldown,
    MusicOptions options,
    BotMetrics metrics,
    ErrorReporter errors) : MusicCommandModule(logger, cooldown, options, metrics, errors)
{
    [SubSlashCommand("play", "Plays a song in your voice channel at a later time, if anyone is there.")]
    [RequireBotConnectPermission]
    public async Task PlayAsync(
        [SlashCommandParameter(Name = "time", Description = "A delay like 30m or 1h30m, or a time like 21:00 or 9pm")] string when,
        [SlashCommandParameter(Name = "query", Description = "URL or Search Query")] string query,
        [SlashCommandParameter(Name = "next", Description = "Insert as next in queue")] bool insertNext = false)
    {
        using var scope = Begin("schedule");

        if (await RefuseIfOnCooldownAsync("schedule")) return;

        var now = time.GetUtcNow();
        if (ScheduleTime.Parse(when, now, Options.FindTimeZone()) is not { } at)
        {
            await RespondWithAsync(ControlResult.Refuse(
                "I couldn't read that time. Use a delay like 30m or 1h30m, or a time like 21:00 or 9pm."));
            return;
        }

        if (at <= now || at - now > ScheduleTime.MaxDelay)
        {
            await RespondWithAsync(ControlResult.Refuse("Pick a time within the next 24 hours."));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.IsComponentsV2));

        try
        {
            var origin = RequestOrigin.From(Context) with { VoiceChannelId = Context.Guild!.VoiceStates[Context.User.Id].ChannelId };
            var play = await scheduler.ScheduleAsync(origin, at, query, insertNext);
            await TryModifyResponseAsync(MusicViews.Scheduled(play), "schedule");
        }
        catch (Exception e)
        {
            await TryModifyResponseAsync(MusicViews.Notice(Errors.Report(e, "schedule"), failure: true), "schedule");
        }
    }

    [SubSlashCommand("list", "Shows what is scheduled in this server.")]
    public async Task ListAsync()
    {
        using var scope = Begin("schedule.list");

        if (Context.Guild is null)
        {
            await RespondWithAsync(ControlResult.Refuse("This command can only be used in a server."));
            return;
        }

        await RespondAsync(InteractionCallback.Message(MusicViews.ScheduleList(scheduler.Pending(Context.Guild.Id)).ToMessage()));
    }

    [SubSlashCommand("cancel", "Cancels a scheduled play.")]
    public async Task CancelAsync(
        [SlashCommandParameter(Name = "id", Description = "Number shown in /schedule list")] int id)
    {
        using var scope = Begin("schedule.cancel");

        if (Context.Guild is null || scheduler.Find(Context.Guild.Id, id) is not { } play)
        {
            await RespondWithAsync(ControlResult.Refuse($"There is no scheduled play {id}."));
            return;
        }

        if (play.Origin.UserId != Context.User.Id && !RequestOrigin.From(Context).HasMoveMembers)
        {
            await RespondWithAsync(ControlResult.Refuse(
                "Only the person who scheduled it, or someone with Move Members, can cancel that."));
            return;
        }

        await RespondWithAsync(scheduler.Cancel(play)
            ? ControlResult.Ok($"Cancelled scheduled play {id}.")
            : ControlResult.Refuse($"Scheduled play {id} has already started."));
    }
}
