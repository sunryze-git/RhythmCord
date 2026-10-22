using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Singularity.Features.Booru.Models;
using Singularity.Features.Booru.Services;
using Singularity.Infrastructure;
using Singularity.Observability;

namespace Singularity.Features.Booru;

public class BooruCommands(
    BooruService booruService,
    CommandCooldown cooldown,
    BooruOptions options,
    BotMetrics metrics,
    ErrorReporter errors,
    ILogger<BooruCommands> logger) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("e621", "Search for posts on e621 with optional filters.")]
    public async Task SearchE621Async(
        [SlashCommandParameter(Name = "tags", Description = "Space-separated search tags")]
        string tags = "",

        [SlashCommandParameter(Name = "rating", Description = "Override content rating filter")]
        BooruRating? rating = null,

        [SlashCommandParameter(Name = "type", Description = "Filter by media file format")]
        BooruMediaType? type = null)
    {
        metrics.Command("e621");
        using var scope = logger.BeginScope("guild={GuildId} user={UserId}", Context.Guild?.Id, Context.User.Id);

        if (!cooldown.TryAcquire("e621", Context.User.Id, options.Cooldown, out var remaining))
        {
            metrics.CooldownHit();
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties
            {
                Content = $"Slow down! Try again in {Math.Ceiling(remaining.TotalSeconds)}s.",
                Flags = MessageFlags.Ephemeral
            }));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage());

        // forces safe rating for posts in SFW chats
        var isNsfwChannel = Context.Channel is TextGuildChannel guildChannel && guildChannel.Nsfw;
        if (!isNsfwChannel)
        {
            rating = BooruRating.Safe;
        }

        ComponentContainerProperties? result = null;
        string? failure = null;
        try
        {
            result = await booruService.GetRandomPostAsync(tags, rating, type);
            metrics.BooruRequest(result is null ? "empty" : "ok");
        }
        catch (Exception ex)
        {
            metrics.BooruRequest("error");
            failure = errors.Report(ex, "e621");
        }

        try
        {
            await ModifyResponseAsync(msg =>
            {
                if (result is null)
                {
                    msg.Content = failure ?? "No posts were found.";
                    return;
                }

                // Components V2 messages cannot carry content or embeds; everything lives in the container.
                msg.Flags = MessageFlags.IsComponentsV2;
                msg.Components = [result];
            });
        }
        catch (Exception ex)
        {
            // The user is left looking at "thinking…"; nothing more can be done for them, but the operator should know.
            errors.Record(ex, "e621.respond", "Could not deliver the e621 result to Discord.");
        }
    }
}
