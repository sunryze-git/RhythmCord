using Singularity.Features.Music.Models;
using Singularity.Features.Music.Player;

namespace Singularity.Features.Music;

/// <summary>What a playback control did. A refusal changes nothing; the message says why.</summary>
internal readonly record struct ControlResult(bool Done, string Message, bool TrackEnded = false)
{
    internal static ControlResult Ok(string message, bool trackEnded = false) => new(true, message, trackEnded);
    internal static ControlResult Refuse(string message) => new(false, message);
}

/// <summary>The playback rules behind both the slash commands and the buttons, so the two can never disagree.</summary>
internal static class MusicControls
{
    internal static ControlResult NothingPlaying => ControlResult.Refuse("No song is currently playing.");

    internal static async Task<ControlResult> SkipAsync(GuildPlayer player, RequestOrigin who)
    {
        var song = player.Snapshot.Current;
        if (song is null) return NothingPlaying;

        // The requester, a moderator, or the only listener can skip outright; everyone else votes.
        if (PlaybackPolicy.CanSkipImmediately(song.RequestedBy == who.UserId, who.CanModerate))
        {
            await player.SkipAsync(song);
            return ControlResult.Ok("Skipped the current song.", trackEnded: true);
        }

        var listeners = who.ListenerCount();
        var needed = PlaybackPolicy.VotesNeeded(listeners);
        var votes = await player.VoteSkipAsync(who.UserId, song);

        if (votes < 0)
            return ControlResult.Refuse("That song already finished.");

        if (!PlaybackPolicy.VotePasses(votes, listeners))
            return ControlResult.Ok($"Skip vote registered ({votes}/{needed}).");

        await player.SkipAsync(song);
        return ControlResult.Ok($"Vote passed ({votes}/{needed}). Skipped the current song.", trackEnded: true);
    }

    internal static async Task<ControlResult> StopAsync(GuildPlayer player, RequestOrigin who)
    {
        if (!who.CanModerate) return NoPermission;
        if (!player.Snapshot.HasWork) return NothingPlaying;

        await player.StopAsync();
        return ControlResult.Ok("Stopped playback and cleared the queue.", trackEnded: true);
    }

    internal static async Task<ControlResult> ShuffleAsync(GuildPlayer player)
    {
        if (!player.Snapshot.HasWork) return NothingPlaying;

        await player.ShuffleAsync();
        return ControlResult.Ok("Queue shuffled.");
    }

    /// <summary>Sets the loop mode, or moves to the next one when <paramref name="mode" /> is null.</summary>
    internal static async Task<ControlResult> LoopAsync(GuildPlayer player, LoopMode? mode)
    {
        var song = player.Snapshot.Current;
        if (song is null) return NothingPlaying;

        return await player.SetLoopAsync(mode, song) switch
        {
            LoopMode.Track => ControlResult.Ok($"Looping {song.Title}."),
            LoopMode.Queue => ControlResult.Ok("Looping the whole queue."),
            LoopMode.Off => ControlResult.Ok("Loop is off."),
            _ => ControlResult.Refuse("That song already finished.")
        };
    }

    internal static async Task<ControlResult> RemoveAsync(GuildPlayer player, RequestOrigin who, MusicTrackNew song)
    {
        if (!who.CanRemove(song))
            return ControlResult.Refuse("Only the person who queued it, or someone with Move Members, can remove that song.");

        return await player.RemoveAsync(song)
            ? ControlResult.Ok($"Removed {song.Title} from the queue.")
            : ControlResult.Refuse("That song is no longer in the queue.");
    }

    private static ControlResult NoPermission =>
        ControlResult.Refuse("You need the Move Members permission (or be alone with me) to do that.");
}
