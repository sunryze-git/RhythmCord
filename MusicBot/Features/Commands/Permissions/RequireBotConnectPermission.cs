using NetCord;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace MusicBot.Features.Commands.Permissions;

// Precondition to ensure the bot has Connect and Speak permissions in the user's voice channel
public class RequireBotConnectPermission : PreconditionAttribute<ApplicationCommandContext>
{
    public override ValueTask<PreconditionResult> EnsureCanExecuteAsync(ApplicationCommandContext context,
        IServiceProvider? services)
    {
        if (context.Guild is null)
            return new ValueTask<PreconditionResult>(PreconditionResult.Fail("This command can only be used in a server."));

        if (!context.Guild.VoiceStates.TryGetValue(context.User.Id, out var voiceState) || voiceState.ChannelId is null)
            return new ValueTask<PreconditionResult>(PreconditionResult.Fail("You must be in a voice channel to use this command."));

        if (!context.Guild.Users.TryGetValue(context.Client.Id, out var self))
            return new ValueTask<PreconditionResult>(PreconditionResult.Fail("Could not find bot user in the guild."));

        var permissions = self.GetChannelPermissions(context.Guild, (ulong)voiceState.ChannelId);
        return permissions.HasFlag(NetCord.Permissions.Connect | NetCord.Permissions.Speak)
            ? new ValueTask<PreconditionResult>(PreconditionResult.Success)
            : new ValueTask<PreconditionResult>(PreconditionResult.Fail("Can't join! I am missing Connect / Speak permissions."));
    }
}
