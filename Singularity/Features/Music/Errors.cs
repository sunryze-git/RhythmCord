using Singularity.Infrastructure;

namespace Singularity.Features.Music;

/// <summary>Every resolver that tried to answer a lookup failed (as opposed to genuinely finding nothing).</summary>
internal sealed class LookupFailedException(string reason, Exception? inner = null)
    : UserFacingException($"I couldn't look that up right now: {reason}. Please try again in a moment.", inner);

/// <summary>No resolver could open an audio stream for a track.</summary>
internal sealed class StreamUnavailableException(string reason, Exception? inner = null)
    : UserFacingException(reason, inner);

/// <summary>A stream broke part-way (for example yt-dlp failed after it started). The message is safe to show.</summary>
internal sealed class MediaStreamException(string reason, Exception? inner = null)
    : UserFacingException(reason, inner);

/// <summary>Joining the user's voice channel failed.</summary>
internal sealed class VoiceJoinException(string message, Exception? inner = null)
    : UserFacingException(message, inner);

/// <summary>
///     A resolver (or the tool behind it) failed for a reason that is safe to show, e.g. yt-dlp's own error message.
///     Not user-facing by itself: the caller decides how to word it.
/// </summary>
internal sealed class ResolverFailedException(string reason, Exception? inner = null) : Exception(reason, inner);
