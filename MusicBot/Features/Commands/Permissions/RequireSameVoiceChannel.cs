using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace MusicBot.Features.Commands.Permissions;

public class RequireSameVoiceChannelAttribute : PreconditionAttribute<ApplicationCommandContext>
{
    public override ValueTask<PreconditionResult> EnsureCanExecuteAsync(ApplicationCommandContext context, IServiceProvider? services)
    {
        if (!context.Guild!.VoiceStates.TryGetValue(context.User.Id, out var userState) || userState.ChannelId == null)
            return new ValueTask<PreconditionResult>(PreconditionResult.Fail("You are not in a voice channel."));

        if (!context.Guild.VoiceStates.TryGetValue(context.Client.Id, out var botState) || botState.ChannelId == null)
            return new ValueTask<PreconditionResult>(PreconditionResult.Fail("I am not in a voice channel."));

        return userState.ChannelId == botState.ChannelId
            ? new ValueTask<PreconditionResult>(PreconditionResult.Success)
            : new ValueTask<PreconditionResult>(PreconditionResult.Fail("You must be in the same voice channel as me!"));
    }
}
