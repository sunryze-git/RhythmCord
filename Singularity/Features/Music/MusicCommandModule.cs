using Microsoft.Extensions.Logging;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Singularity.Features.Music.Views;
using Singularity.Infrastructure;
using Singularity.Observability;

namespace Singularity.Features.Music;

/// <summary>What the music slash commands have in common: usage counting, log context, cooldowns and replies.</summary>
public abstract class MusicCommandModule(
    ILogger logger,
    CommandCooldown cooldown,
    MusicOptions options,
    BotMetrics metrics,
    ErrorReporter errors) : ApplicationCommandModule<ApplicationCommandContext>
{
    private protected MusicOptions Options => options;
    private protected ErrorReporter Errors => errors;

    // Counts the command (by name only) and tags this call's logs with guild/user context (console only).
    private protected IDisposable? Begin(string command)
    {
        metrics.Command(command);
        return logger.BeginScope("guild={GuildId} user={UserId}", Context.Guild?.Id, Context.User.Id);
    }

    private protected async Task<bool> RefuseIfOnCooldownAsync(string command)
    {
        if (cooldown.TryAcquire(command, Context.User.Id, options.PlayCooldown, out var remaining)) return false;

        metrics.CooldownHit();
        await RespondWithAsync(ControlResult.Refuse($"Slow down! Try again in {Math.Ceiling(remaining.TotalSeconds)}s."));
        return true;
    }

    // Refusals are only shown to the person who asked.
    private protected Task RespondWithAsync(ControlResult result) =>
        RespondAsync(InteractionCallback.Message(MusicViews.Notice(result.Message, failure: !result.Done).ToMessage(ephemeral: !result.Done)));

    private protected async Task TryModifyResponseAsync(MessageView view, string command)
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
}
