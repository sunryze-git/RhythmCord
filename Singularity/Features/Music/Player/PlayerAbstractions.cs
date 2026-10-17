using Singularity.Infrastructure;

namespace Singularity.Features.Music.Player;

/// <summary>Plays one decoded stream to the voice channel. The real one runs ffmpeg and sends Opus to Discord.</summary>
internal interface ITrackPlayer
{
    /// <summary>How far into the current track playback has got.</summary>
    TimeSpan Position { get; }

    /// <summary>Plays the stream until it ends or <paramref name="cancellationToken" /> is cancelled (which returns quietly).</summary>
    Task PlayAsync(Stream input, CancellationToken cancellationToken);
}

/// <summary>The part of the voice connection the player needs.</summary>
internal interface IVoiceSession
{
    /// <summary>Raised when the connection drops; the argument says whether it will reconnect by itself.</summary>
    event Action<bool>? Disconnected;

    /// <summary>People (besides the bot) in the bot's voice channel, or null when unknown.</summary>
    int? ListenerCount();

    Task LeaveAsync();
}

/// <summary>Where the player reports problems to users (a text channel in production).</summary>
internal interface IPlayerNotifier
{
    Task SendAsync(string message);
}

/// <summary>Thrown when a command targets a guild session that is already shutting down.</summary>
internal sealed class SessionClosedException() : UserFacingException("The player is shutting down. Please try again in a moment.");
