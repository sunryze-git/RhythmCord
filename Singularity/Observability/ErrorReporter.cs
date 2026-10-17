using System.Reflection;
using Microsoft.Extensions.Logging;
using Singularity.Infrastructure;

namespace Singularity.Observability;

/// <summary>
///     The single place a handled command error is recorded and turned into text for the user, so every command
///     reports the same way: the log (right level), the metrics ("errors" by type) and the reply.
///     <list type="bullet">
///         <item><see cref="UserFacingException" />: expected. Logged at Info (Warning if it wraps a cause), counted as
///         "&lt;area&gt;.user", and its own message is shown.</item>
///         <item>Anything else: a bug or an outage. Logged at Error with the stack trace, counted as
///         "&lt;area&gt;.unexpected", and only a generic apology is shown (internals are never leaked to Discord).</item>
///     </list>
/// </summary>
public sealed class ErrorReporter(BotMetrics metrics, ILogger<ErrorReporter> logger)
{
    public const string GenericMessage = "Something went wrong on my end. It has been logged. Please try again.";

    /// <summary>Records the error and returns the message to show the user.</summary>
    public string Report(Exception exception, string area)
    {
        exception = Unwrap(exception);

        if (exception is UserFacingException user)
        {
            if (user.InnerException is null)
                logger.LogInformation("{Area}: {Message}", area, user.Message);
            else
                logger.LogWarning(user.InnerException, "{Area}: {Message}", area, user.Message);

            metrics.Error($"{area}.user");
            return user.Message;
        }

        logger.LogError(exception, "{Area} failed unexpectedly", area);
        metrics.Error($"{area}.unexpected");
        return GenericMessage;
    }

    /// <summary>Records an error that is not tied to a command (background work, shutdown, ...).</summary>
    public void Record(Exception exception, string type, string what)
    {
        logger.LogError(Unwrap(exception), "{What}", what);
        metrics.Error(type);
    }

    private static Exception Unwrap(Exception exception) => exception switch
    {
        TargetInvocationException { InnerException: { } inner } => Unwrap(inner),
        AggregateException { InnerExceptions.Count: 1 } aggregate => Unwrap(aggregate.InnerExceptions[0]),
        _ => exception
    };
}
