using Microsoft.Extensions.Logging;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Resolvers;
using Singularity.Features.Music.Services;
using Singularity.Features.Music.Views;
using Singularity.Observability;

namespace Singularity.Features.Music;

/// <summary>A play waiting for its time. The origin holds the voice and text channel it was scheduled from.</summary>
internal sealed record ScheduledPlay(int Id, DateTimeOffset At, string Query, IReadOnlyList<MusicTrackNew> Songs, bool Next, RequestOrigin Origin)
{
    internal ulong GuildId => Origin.Guild!.Id;
    internal ulong VoiceChannelId => Origin.VoiceChannelId!.Value;
}

/// <summary>
///     /schedule: holds plays until their time, then starts them in the voice channel they were scheduled from, but only
///     if someone is there. Kept in memory, so a restart forgets them.
/// </summary>
public sealed class MusicScheduler : IDisposable
{
    private readonly Lock _lock = new();
    private readonly Dictionary<int, (ScheduledPlay Play, CancellationTokenSource Cancel)> _pending = [];
    private readonly GuildAudioInstanceOrchestrator _orchestrator;
    private readonly ITrackSource _source;
    private readonly MusicOptions _options;
    private readonly TimeProvider _time;
    private readonly BotMetrics _metrics;
    private readonly ErrorReporter _errors;
    private readonly ILogger<MusicScheduler> _logger;
    private int _lastId;

    internal MusicScheduler(
        GuildAudioInstanceOrchestrator orchestrator,
        ITrackSource source,
        MusicOptions options,
        TimeProvider time,
        BotMetrics metrics,
        ErrorReporter errors,
        ILogger<MusicScheduler> logger)
    {
        _orchestrator = orchestrator;
        _source = source;
        _options = options;
        _time = time;
        _metrics = metrics;
        _errors = errors;
        _logger = logger;
    }

    /// <summary>Looks the query up now and again at <paramref name="at" />, when it is queued.</summary>
    internal async Task<ScheduledPlay> ScheduleAsync(RequestOrigin origin, DateTimeOffset at, string query, bool next)
    {
        var guildId = origin.Guild!.Id;
        lock (_lock) EnsureRoom(guildId);

        var songs = await _source.ResolveSongsAsync(query);
        if (songs.Count == 0) throw new SearchException("No songs found for the provided term.");

        var sameSongLater = songs.Count == 1 ? songs[0].Url : query;

        ScheduledPlay play;
        var cancel = new CancellationTokenSource();
        lock (_lock)
        {
            EnsureRoom(guildId);
            play = new ScheduledPlay(++_lastId, at, sameSongLater, songs, next, origin);
            _pending[play.Id] = (play, cancel);
        }

        _ = WaitAndStartAsync(play, cancel.Token);
        return play;
    }

    internal IReadOnlyList<ScheduledPlay> Pending(ulong guildId)
    {
        lock (_lock) return _pending.Values.Select(p => p.Play).Where(p => p.GuildId == guildId).OrderBy(p => p.At).ToList();
    }

    internal ScheduledPlay? Find(ulong guildId, int id)
    {
        lock (_lock) return _pending.TryGetValue(id, out var entry) && entry.Play.GuildId == guildId ? entry.Play : null;
    }

    /// <summary>False if it already started or was cancelled.</summary>
    internal bool Cancel(ScheduledPlay play)
    {
        CancellationTokenSource cancel;
        lock (_lock)
        {
            if (!_pending.Remove(play.Id, out var entry)) return false;
            cancel = entry.Cancel;
        }

        cancel.Cancel();
        cancel.Dispose();
        return true;
    }

    // Callers hold _lock.
    private void EnsureRoom(ulong guildId)
    {
        if (_pending.Values.Count(p => p.Play.GuildId == guildId) >= _options.MaxScheduledPlays)
            throw new SearchException($"This server already has {_options.MaxScheduledPlays} scheduled plays. Cancel one first.");
    }

    private async Task WaitAndStartAsync(ScheduledPlay play, CancellationToken token)
    {
        try
        {
            var wait = play.At - _time.GetUtcNow();
            await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, _time, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!Take(play)) return;

        using var scope = _logger.BeginScope("guild={GuildId} user={UserId}", play.GuildId, play.Origin.UserId);
        try
        {
            await StartAsync(play);
        }
        catch (Exception ex)
        {
            _errors.Record(ex, "schedule.start", "A scheduled play failed to start.");
        }
    }

    private bool Take(ScheduledPlay play)
    {
        lock (_lock)
        {
            if (!_pending.Remove(play.Id, out var entry)) return false;
            entry.Cancel.Dispose();
            return true;
        }
    }

    private async Task StartAsync(ScheduledPlay play)
    {
        var client = play.Origin.Client;
        if (!client.Cache.Guilds.TryGetValue(play.GuildId, out var guild)) return;

        if (!guild.VoiceStates.Any(kv => kv.Key != client.Id && kv.Value.ChannelId == play.VoiceChannelId))
        {
            _logger.LogInformation("Scheduled play #{Id} dropped: nobody is in its voice channel.", play.Id);
            return;
        }

        var origin = play.Origin with { Guild = guild };
        if (guild.VoiceStates.GetValueOrDefault(client.Id)?.ChannelId is { } botChannel && botChannel != play.VoiceChannelId)
        {
            await PostAsync(origin, MusicViews.ScheduledPlayFailed(play, "I'm playing in another voice channel."));
            return;
        }

        GuildAudioInstance? manager = null;
        try
        {
            manager = _orchestrator.GetOrCreateManager(play.GuildId);
            var songs = await manager.EnqueueAsync(origin, play.Query, play.Next);
            await PostAsync(origin, MusicViews.ScheduledPlayStarted(songs, play));
        }
        catch (Exception e)
        {
            // A session that never managed to join voice would otherwise linger as an empty shell.
            if (manager is { Voice.IsJoined: false })
                await _orchestrator.CloseManagerAsync(play.GuildId);

            await PostAsync(origin, MusicViews.ScheduledPlayFailed(play, _errors.Report(e, "schedule")));
        }
    }

    private async Task PostAsync(RequestOrigin origin, MessageView view)
    {
        try
        {
            await origin.Client.Rest.SendMessageAsync(origin.ChannelId, view.ToChannelMessage());
        }
        catch (Exception ex)
        {
            // Most often missing "Send Messages" permission. Nobody sees the message, so the operator must.
            _metrics.Error("notify.failed");
            _logger.LogWarning(ex, "Could not post a scheduled play message in the text channel (missing permissions?).");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var (_, cancel) in _pending.Values)
            {
                cancel.Cancel();
                cancel.Dispose();
            }

            _pending.Clear();
        }
    }
}
