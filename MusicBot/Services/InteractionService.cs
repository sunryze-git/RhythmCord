using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace MusicBot.Services;

public class InteractionService(
    ApplicationCommandService<ApplicationCommandContext> applicationCommandService, GatewayClient client)
{
    internal async ValueTask OnClientOnInteractionCreate(Interaction interaction)
    {
        // Check if the interaction is an application command interaction
        if (interaction is not ApplicationCommandInteraction applicationCommandInteraction) return;

        // Execute the command
        var result = await applicationCommandService.ExecuteAsync(new ApplicationCommandContext(applicationCommandInteraction, client));

        // Check if the execution failed
        if (result is not IFailResult failResult) return;

        // Return the error message to the user if the execution failed
        try
        {
            await interaction.SendResponseAsync(InteractionCallback.Message(failResult.Message));
        }
        catch
        {
            // ignored
        }
    }
}