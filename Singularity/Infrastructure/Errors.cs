namespace Singularity.Infrastructure;

/// <summary>
///     An error whose message is safe and useful to show to the person who ran the command ("no songs found",
///     "e621 is rate limiting me", "join a voice channel first"). Anything that is NOT one of these is treated as a bug:
///     logged with its stack trace and answered with a generic apology, never with its own message.
///     Give it an inner exception when the cause matters to whoever runs the bot; it is then logged as a warning.
/// </summary>
public abstract class UserFacingException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A known configuration or environment problem at startup: printed as one line, with no stack trace.</summary>
public sealed class StartupException(string message, Exception? inner = null) : Exception(message, inner);
