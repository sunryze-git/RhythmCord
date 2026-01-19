using Microsoft.Extensions.Logging;

using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace MusicBot.Features.Commands;

public class FunCommands(ILogger<FunCommands> logger) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("snap", "Administrative command only.")]
    public async Task SnapAsync(
            [SlashCommandParameter(Name = "channel", Description = "Target Channel ID.")]
            string channelId)
        // remove everyone from the VC you are in
    {
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
        var guild = await Context.Client.Rest.GetGuildAsync(Context.Guild.Id);
        var usersInVc = Context.Guild.VoiceStates
            .Where(vs => vs.Value.ChannelId == channelIdValue)
            .Select(async vs => await guild.GetUserAsync(vs.Key))
            .ToArray();
        if (usersInVc.Length == 0)
        {
            await ModifyResponseAsync(message =>
                message.Content = "There is nobody in the voice channel to remove.");
            return;
        }

        // Remove the users from the voice channel
        foreach (var user in usersInVc)
            try
            {
                var userObject = await user;
                // Attempt to disconnect the user from the voice channel
                await userObject.ModifyAsync(act => { act.ChannelId = 0; });
            }
            catch (Exception ex)
            {
                // Log the error if we cannot remove a user
                logger.LogWarning("Failed to remove user {UserId} from voice channel: {ExMessage}", user.Id,
                    ex.Message);
            }

        await ModifyResponseAsync(message =>
            message.Content =
                $"Removed {usersInVc.Length} users from the voice channel. If nobody was removed, I do not have permission to do so.");
    }
}
