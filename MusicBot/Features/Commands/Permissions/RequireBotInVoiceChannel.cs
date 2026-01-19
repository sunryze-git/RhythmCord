using Microsoft.Extensions.DependencyInjection;

using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace MusicBot.Features.Commands.Permissions;

// Precondition to ensure the bot is already in a voice channel in the guild
public class RequireBotInVoiceChannel : PreconditionAttribute<ApplicationCommandContext>
{
    public override ValueTask<PreconditionResult> EnsureCanExecuteAsync(ApplicationCommandContext context, IServiceProvider? serviceProvider)
    {
        if (serviceProvider is null)
            return new ValueTask<PreconditionResult>(PreconditionResult.Fail("Service provider is not available."));

        var orchestrator = serviceProvider.GetService<GuildAudioInstanceOrchestrator>();

        if (orchestrator != null && orchestrator.GuildIsActive(context.Guild!.Id))
            return new ValueTask<PreconditionResult>(PreconditionResult.Success);

        return new ValueTask<PreconditionResult>(PreconditionResult.Fail("The bot is not in a voice channel in this server."));
    }
}
