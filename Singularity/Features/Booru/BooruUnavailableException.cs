using Singularity.Infrastructure;

namespace Singularity.Features.Booru;

/// <summary>e621 could not be reached or answered badly. The message says why, in words fit for the user.</summary>
public sealed class BooruUnavailableException(string message, Exception? inner = null) : UserFacingException(message, inner);
