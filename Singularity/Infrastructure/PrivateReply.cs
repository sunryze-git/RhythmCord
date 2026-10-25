using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

namespace Singularity.Infrastructure;

internal static class PrivateReply
{
    /// <summary>Tells the user something only they can see, whether or not the interaction was already answered.</summary>
    internal static async Task SendAsync(Interaction interaction, string message, ILogger logger)
    {
        var properties = new InteractionMessageProperties
        {
            Content = message,
            Flags = MessageFlags.Ephemeral,
            AllowedMentions = AllowedMentionsProperties.None
        };

        try
        {
            await interaction.SendResponseAsync(InteractionCallback.Message(properties));
            return;
        }
        catch (Exception)
        {
            // Already acknowledged (deferred or answered): fall through to a follow-up.
        }

        try
        {
            await interaction.SendFollowupMessageAsync(properties);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not tell the user about a failure.");
        }
    }
}
