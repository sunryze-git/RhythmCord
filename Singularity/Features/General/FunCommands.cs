using Microsoft.Extensions.Logging;

using NetCord;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using Singularity.Observability;

namespace Singularity.Features.General;

public class FunCommands(ILogger<FunCommands> logger, BotMetrics metrics) : ApplicationCommandModule<ApplicationCommandContext>
{
    // remove everyone from the given voice channel
    [SlashCommand("snap", "Administrative command only.")]
    [RequireUserPermissions<ApplicationCommandContext>(Permissions.MoveUsers)]
    public async Task SnapAsync([SlashCommandParameter(Name = "channel", Description = "Target Channel ID.")] string channelId)
    {
        metrics.Command("snap");
        using var scope = logger.BeginScope("guild={GuildId} user={UserId}", Context.Guild?.Id, Context.User.Id);

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        // Determine if this is a valid ID
        if (!ulong.TryParse(channelId, out var channelIdValue))
        {
            await ModifyResponseAsync(message =>
                message.Content = "Invalid channel ID provided. Please provide a valid channel ID.");
            return;
        }

        // Is this in a guild?
        if (Context.Guild == null)
        {
            await ModifyResponseAsync(message =>
                message.Content = "This command can only be used in a guild.");
            return;
        }

        // Get the users in that voice channel
        var guildId = Context.Guild.Id;
        var userIds = Context.Guild.VoiceStates
            .Where(vs => vs.Value.ChannelId == channelIdValue)
            .Select(vs => vs.Key)
            .ToArray();
        if (userIds.Length == 0)
        {
            await ModifyResponseAsync(message =>
                message.Content = "There is nobody in the voice channel to remove.");
            return;
        }

        // Remove the users from the voice channel
        var removed = 0;
        foreach (var userId in userIds)
        {
            try
            {
                // Attempt to disconnect the user from the voice channel
                await Context.Client.Rest.ModifyGuildUserAsync(guildId, userId, act => { act.ChannelId = 0; });
                removed++;
            }
            catch (Exception ex)
            {
                // Log the error if we cannot remove a user
                logger.LogWarning("Failed to remove user {UserId} from voice channel: {ExMessage}", userId, ex.Message);
            }
        }

        await ModifyResponseAsync(message =>
            message.Content = removed == userIds.Length
                ? $"Removed {removed} user(s) from the voice channel."
                : $"Removed {removed} of {userIds.Length} user(s). I may be missing permission to move the others.");
    }
}
