using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services;
using NetCord.Services.ComponentInteractions;
using Singularity.Observability;

namespace Singularity.Infrastructure;

/// <summary>What happens when a button or menu fails: the problem is logged and counted, and only the person who used it hears about it.</summary>
public sealed class ComponentErrorHandler<TContext> : IComponentInteractionResultHandler<TContext>
    where TContext : IComponentInteractionContext
{
    public async ValueTask HandleResultAsync(
        IExecutionResult result,
        TContext context,
        GatewayClient? client,
        ILogger logger,
        IServiceProvider services)
    {
        var message = CommandErrorHandler.Decide(
            result,
            HandlerName(context.Interaction.Data.CustomId),
            services.GetRequiredService<ErrorReporter>(),
            services.GetRequiredService<BotMetrics>(),
            logger);

        if (message is not null) await PrivateReply.SendAsync(context.Interaction, message, logger);
    }

    // Custom IDs can carry tokens; only the part naming the handler is safe to use as a metric tag.
    internal static string HandlerName(string customId) => customId.Split(':', 2)[0];
}
