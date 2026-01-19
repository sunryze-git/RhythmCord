using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace MusicBot.Features.Commands.Permissions;

public class RequireUserVoiceAttribute : PreconditionAttribute<ApplicationCommandContext>
{
    public override ValueTask<PreconditionResult> EnsureCanExecuteAsync(ApplicationCommandContext context, IServiceProvider? services)
    {
        if (context.Guild == null)
            return new ValueTask<PreconditionResult>(PreconditionResult.Fail("Must be in a server."));

        if (context.Guild.VoiceStates.TryGetValue(context.User.Id, out var state) && state.ChannelId != null)
            return new ValueTask<PreconditionResult>(PreconditionResult.Success);

        return new ValueTask<PreconditionResult>(PreconditionResult.Fail("You must be in a voice channel to use this command!"));
    }
}
