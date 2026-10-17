using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using Singularity.Observability;

namespace Singularity.Infrastructure;

/// <summary>
///     What happens when a slash command fails before or while it runs: a failed precondition ("you must be in a voice
///     channel"), an unparsable argument, or an exception nobody handled. Every case ends the same way: the problem is
///     logged and counted, and the person who ran the command gets a reply only they can see.
/// </summary>
public sealed class CommandErrorHandler : IApplicationCommandResultHandler<ApplicationCommandContext>
{
    public async ValueTask HandleResultAsync(
        IExecutionResult result,
        ApplicationCommandContext context,
        GatewayClient? client,
        ILogger logger,
        IServiceProvider services)
    {
        var message = Decide(
            result,
            context.Interaction.Data.Name,
            services.GetRequiredService<ErrorReporter>(),
            services.GetRequiredService<BotMetrics>(),
            logger);

        if (message is not null) await ReplyAsync(context, message, logger);
    }

    /// <summary>Logs and counts a failed command and returns what to tell the user, or null if nothing failed.</summary>
    internal static string? Decide(IExecutionResult result, string command, ErrorReporter reporter, BotMetrics metrics, ILogger logger)
    {
        if (result is not IFailResult failure) return null;

        if (result is IExceptionResult { Exception: { } exception })
            return reporter.Report(exception, command);

        // Precondition / argument problems: the framework's message is written for the user.
        logger.LogInformation("/{Command} was refused: {Reason}", command, failure.Message);
        metrics.Error($"{command}.refused");
        return failure.Message;
    }

    private static async Task ReplyAsync(ApplicationCommandContext context, string message, ILogger logger)
    {
        var properties = new InteractionMessageProperties
        {
            Content = message,
            Flags = MessageFlags.Ephemeral,
            AllowedMentions = AllowedMentionsProperties.None
        };

        try
        {
            await context.Interaction.SendResponseAsync(InteractionCallback.Message(properties));
            return;
        }
        catch (Exception)
        {
            // Already acknowledged (deferred or answered): fall through to a follow-up.
        }

        try
        {
            await context.Interaction.SendFollowupMessageAsync(properties);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not tell the user about a command failure.");
        }
    }
}
