using Microsoft.Extensions.DependencyInjection;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using Singularity.Features.Music.Models;

namespace Singularity.Features.Music.Permissions;

// Precondition to ensure the bot is already in a voice channel in the guild
internal class RequireBotInVoiceChannel : PreconditionAttribute<ApplicationCommandContext>
{
    public override ValueTask<PreconditionResult> EnsureCanExecuteAsync(ApplicationCommandContext context, IServiceProvider? serviceProvider)
    {
        if (context.Guild is null)
            return new ValueTask<PreconditionResult>(PreconditionResult.Fail("This command can only be used in a server."));

        if (serviceProvider is null)
            return new ValueTask<PreconditionResult>(PreconditionResult.Fail("Service provider is not available."));

        var orchestrator = serviceProvider.GetService<GuildAudioInstanceOrchestrator>();

        if (orchestrator != null && orchestrator.GuildIsActive(context.Guild.Id))
            return new ValueTask<PreconditionResult>(PreconditionResult.Success);

        return new ValueTask<PreconditionResult>(PreconditionResult.Fail("The bot is not in a voice channel in this server."));
    }
}
