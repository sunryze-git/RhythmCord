using Singularity.Features.Music.Player;
using Singularity.Features.Music.Resolvers;
using Singularity.Features.Music.Services;

namespace Singularity.Features.Music.Models;

/// <summary>Everything one guild needs for music: the player, its voice connection and the lookup pipeline.</summary>
internal sealed class GuildAudioInstance(
    GuildPlayer player,
    DiscordVoiceSession voice,
    ChannelNotifier notifier,
    ITrackSource source,
    MusicOptions options)
{
    internal GuildPlayer Player => player;
    internal DiscordVoiceSession Voice => voice;

    internal void Bind(ulong guildId, Action closeRequested) => player.Initialize(guildId, closeRequested);

    /// <summary>
    ///     Resolves the query and joins the user's voice channel (if needed) at the same time, then queues the result.
    ///     Nothing is queued unless both succeed, and a voice join made for a failed lookup is undone.
    ///     Returns the tracks that were actually queued (the queue has a size limit).
    /// </summary>
    internal Task<IReadOnlyList<MusicTrackNew>> EnqueueAsync(RequestOrigin origin, string term, bool next) =>
        EnqueueAsync(origin, ResolveAsync(term), next);

    /// <summary>Like the query overload, for tracks that were already looked up (a search result).</summary>
    internal Task<IReadOnlyList<MusicTrackNew>> EnqueueAsync(RequestOrigin origin, IReadOnlyList<MusicTrackNew> tracks, bool next) =>
        EnqueueAsync(origin, Task.FromResult(tracks), next);

    private async Task<IReadOnlyList<MusicTrackNew>> EnqueueAsync(
        RequestOrigin origin, Task<IReadOnlyList<MusicTrackNew>> resolveTask, bool next)
    {
        notifier.Update(origin);

        var joinTask = voice.JoinAsync(origin);

        try
        {
            await Task.WhenAll(resolveTask, joinTask);
        }
        catch
        {
            // Both tasks have finished by now. If we joined voice just for this request, leave again.
            if (joinTask.IsCompletedSuccessfully && joinTask.Result)
                player.CloseIfUnused();

            throw;
        }

        var result = await player.EnqueueAsync(await resolveTask, next, origin.UserId);
        if (result.Accepted.Count == 0)
            throw new SearchException($"The queue is full ({options.MaxQueueLength} tracks).");

        return result.Accepted;
    }

    private async Task<IReadOnlyList<MusicTrackNew>> ResolveAsync(string term)
    {
        var songs = await source.ResolveSongsAsync(term);
        if (songs.Count == 0) throw new SearchException("No songs found for the provided term.");
        return songs;
    }
}
