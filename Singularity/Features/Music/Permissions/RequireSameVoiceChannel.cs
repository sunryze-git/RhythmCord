using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace Singularity.Features.Music.Permissions;

internal class RequireSameVoiceChannelAttribute : PreconditionAttribute<ApplicationCommandContext>
{
    public override ValueTask<PreconditionResult> EnsureCanExecuteAsync(ApplicationCommandContext context, IServiceProvider? services) =>
        new(Result(VoiceChecks.SameChannel(context.Guild, context.User.Id, context.Client.Id)));

    internal static PreconditionResult Result(string? problem) =>
        problem is null ? PreconditionResult.Success : PreconditionResult.Fail(problem);
}

internal class RequireSameVoiceChannelIfConnectedAttribute : PreconditionAttribute<ApplicationCommandContext>
{
    public override ValueTask<PreconditionResult> EnsureCanExecuteAsync(ApplicationCommandContext context, IServiceProvider? services) =>
        new(RequireSameVoiceChannelAttribute.Result(VoiceChecks.SameChannelIfConnected(context.Guild, context.User.Id, context.Client.Id)));
}
