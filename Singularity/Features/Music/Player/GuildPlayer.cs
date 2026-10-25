using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Services;
using Singularity.Infrastructure;
using Singularity.Observability;

namespace Singularity.Features.Music.Player;

/// <summary>
///     One server's music player, written as a small actor: every command is a message on a channel and a single loop
///     handles them one at a time. All playback state (queue, current track, loop mode, skip votes, idle/close state)
///     is owned by that loop and never touched from anywhere else, so there are no locks and no races to reason about.
///     Callers read <see cref="Snapshot" /> (an immutable copy) and ask for changes through the async methods.
///     Playing a track runs on its own task and reports back with a message when it ends.
/// </summary>
internal sealed class GuildPlayer : IAsyncDisposable
{
    private enum Outcome { Completed, Cancelled, Failed }

    private abstract record Message;
    private sealed record EnqueueMessage(IReadOnlyList<MusicTrackNew> Tracks, bool Next, TaskCompletionSource<EnqueueResult> Reply) : Message;
    private sealed record SkipMessage(MusicTrackNew? Expected, bool ClearQueue, TaskCompletionSource Reply) : Message;
    private sealed record ShuffleMessage(TaskCompletionSource Reply) : Message;
    private sealed record LoopMessage(LoopMode? Mode, MusicTrackNew? Expected, TaskCompletionSource<LoopMode?> Reply) : Message;
    private sealed record RemoveMessage(MusicTrackNew Track, TaskCompletionSource<bool> Reply) : Message;
    private sealed record VoteMessage(ulong UserId, MusicTrackNew? Expected, TaskCompletionSource<int> Reply) : Message;
    private sealed record CloseMessage(string Reason, bool OnlyIfUnused, bool FromDispose) : Message;
    private sealed record TrackEndedMessage(int Generation, Outcome Outcome) : Message;
    private sealed record IdleElapsedMessage(int Generation) : Message;

    private readonly ITrackSource _source;
    private readonly ITrackPlayer _player;
    private readonly IVoiceSession _voice;
    private readonly IPlayerNotifier _notifier;
    private readonly BotMetrics _metrics;
    private readonly MusicOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<GuildPlayer> _logger;

    private readonly Channel<Message> _inbox = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _watchCts = new();

    // --- state owned by the loop ---
    private readonly TrackQueue _queue;
    private readonly HashSet<ulong> _skipVotes = [];
    private MusicTrackNew? _current;
    private LoopMode _loop;
    private bool _closing;
    private CancellationTokenSource? _trackCts;
    private int _generation;
    private CancellationTokenSource? _idleCts;
    private int _idleGeneration;

    // --- shared, thread-safe ---
    private volatile PlayerSnapshot _snapshot = PlayerSnapshot.Empty;
    private Task _loopTask = Task.CompletedTask;
    private Task _watchTask = Task.CompletedTask;
    private ulong _guildId;
    private Action? _closeRequested;
    private int _closeRaised;
    private int _disposed;

    public GuildPlayer(
        ITrackSource source,
        ITrackPlayer player,
        IVoiceSession voice,
        IPlayerNotifier notifier,
        BotMetrics metrics,
        MusicOptions options,
        TimeProvider time,
        ILogger<GuildPlayer> logger)
    {
        _source = source;
        _player = player;
        _voice = voice;
        _notifier = notifier;
        _metrics = metrics;
        _options = options;
        _time = time;
        _logger = logger;
        _queue = new TrackQueue(options.MaxQueueLength);
    }

    /// <summary>Current state; never blocks and is never null.</summary>
    internal PlayerSnapshot Snapshot => _snapshot;

    internal TimeSpan Position => _player.Position;

    /// <summary>
    ///     Starts the loop. <paramref name="closeRequested" /> is invoked (once) when the player decides the session
    ///     should end (idle, alone, disconnected, closed on request); the owner then disposes it.
    /// </summary>
    internal void Initialize(ulong guildId, Action closeRequested)
    {
        _guildId = guildId;
        _closeRequested = closeRequested;
        _voice.Disconnected += OnDisconnected;

        BeginIdle();
        _loopTask = Task.Run(RunAsync);
        _watchTask = Task.Run(() => WatchAsync(_watchCts.Token));
    }

    // ---------------------------------------------------------------- commands

    internal Task<EnqueueResult> EnqueueAsync(IReadOnlyList<MusicTrackNew> tracks, bool next, ulong requestedBy)
    {
        foreach (var track in tracks) track.RequestedBy = requestedBy;
        return Ask<EnqueueResult>(reply => new EnqueueMessage(tracks, next, reply));
    }

    /// <summary>Skips the current track. If <paramref name="expected" /> is given and is no longer current, does nothing.</summary>
    internal Task SkipAsync(MusicTrackNew? expected = null) => Ask(reply => new SkipMessage(expected, false, reply));

    /// <summary>Clears the queue and stops the current track.</summary>
    internal Task StopAsync() => Ask(reply => new SkipMessage(null, true, reply));

    internal Task ShuffleAsync() => Ask(reply => new ShuffleMessage(reply));

    /// <summary>
    ///     Sets the loop mode, or moves to the next one when <paramref name="mode" /> is null. Returns the new mode, or
    ///     null if nothing is playing.
    /// </summary>
    internal Task<LoopMode?> SetLoopAsync(LoopMode? mode, MusicTrackNew? expected = null) =>
        Ask<LoopMode?>(reply => new LoopMessage(mode, expected, reply));

    /// <summary>Takes a waiting track out of the queue; false if it already started playing or was removed.</summary>
    internal Task<bool> RemoveAsync(MusicTrackNew track) => Ask<bool>(reply => new RemoveMessage(track, reply));

    /// <summary>Registers a skip vote and returns the vote count, or -1 if that track is no longer playing.</summary>
    internal Task<int> VoteSkipAsync(ulong userId, MusicTrackNew? expected = null) =>
        Ask<int>(reply => new VoteMessage(userId, expected, reply));

    internal void RequestClose() => Post(new CloseMessage("requested", false, false));

    /// <summary>Closes the player only if it never got anything to play (e.g. a lookup failed after joining voice).</summary>
    internal void CloseIfUnused() => Post(new CloseMessage("unused", true, false));

    // ---------------------------------------------------------------- the loop

    private async Task RunAsync()
    {
        using var scope = _logger.BeginScope("guild={GuildId}", _guildId);
        try
        {
            await foreach (var message in _inbox.Reader.ReadAllAsync())
            {
                try
                {
                    Handle(message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Player failed handling {Message}.", message.GetType().Name);
                    _metrics.Error("player.loop");
                    Fail(message, ex);
                }

                Publish();
            }
        }
        finally
        {
            _inbox.Writer.TryComplete();
            while (_inbox.Reader.TryRead(out var leftover)) Fail(leftover, new SessionClosedException());
            Publish();
        }
    }

    private void Handle(Message message)
    {
        switch (message)
        {
            case EnqueueMessage m: OnEnqueue(m); break;
            case SkipMessage m: OnSkip(m); break;
            case ShuffleMessage m:
                _queue.Shuffle();
                Complete(m.Reply);
                break;
            case LoopMessage m: OnLoop(m); break;
            case RemoveMessage m: Complete(m.Reply, _queue.Remove(m.Track)); break;
            case VoteMessage m: OnVote(m); break;
            case TrackEndedMessage m: OnTrackEnded(m); break;
            case CloseMessage m: OnClose(m); break;
            case IdleElapsedMessage m:
                if (m.Generation == _idleGeneration && _current is null && !_closing)
                {
                    _logger.LogInformation("Idle for {Minutes} min. Leaving.", (int)_options.IdleTimeout.TotalMinutes);
                    OnClose(new CloseMessage("idle", false, false));
                }

                break;
        }
    }

    private void OnEnqueue(EnqueueMessage m)
    {
        if (_closing)
        {
            m.Reply.TrySetException(new SessionClosedException());
            return;
        }

        var added = _queue.Add(m.Tracks, m.Next);
        _metrics.TracksQueued(added);

        if (_current is null && added > 0) StartNext();

        Complete(m.Reply, new EnqueueResult(added == m.Tracks.Count ? m.Tracks : m.Tracks.Take(added).ToList()));
    }

    private void OnSkip(SkipMessage m)
    {
        if (m.Expected is not null && !ReferenceEquals(m.Expected, _current))
        {
            Complete(m.Reply);
            return;
        }

        if (m.ClearQueue)
        {
            _queue.Clear();
            _loop = LoopMode.Off;
        }

        if (_current is not null)
        {
            if (_loop == LoopMode.Track) _loop = LoopMode.Off;
            _trackCts?.Cancel(); // the track task reports back with TrackEnded, which moves on to the next track
        }

        Complete(m.Reply);
    }

    private void OnLoop(LoopMessage m)
    {
        if (_current is null || (m.Expected is not null && !ReferenceEquals(m.Expected, _current)))
        {
            Complete(m.Reply, null);
            return;
        }

        _loop = m.Mode ?? _loop.Next();
        Complete(m.Reply, _loop);
    }

    private void OnVote(VoteMessage m)
    {
        if (_current is null || (m.Expected is not null && !ReferenceEquals(m.Expected, _current)))
        {
            Complete(m.Reply, -1);
            return;
        }

        _skipVotes.Add(m.UserId);
        Complete(m.Reply, _skipVotes.Count);
    }

    private void OnTrackEnded(TrackEndedMessage m)
    {
        if (m.Generation != _generation) return; // a track that was already replaced

        _trackCts?.Dispose();
        _trackCts = null;
        var finished = _current;

        if (m.Outcome == Outcome.Cancelled && !_closing) _metrics.TrackSkipped();

        if (!_closing && finished is not null)
        {
            // Looping a track plays it again. Works for every source, since each play opens a fresh stream.
            if (m.Outcome == Outcome.Completed && _loop == LoopMode.Track)
            {
                StartTrack(finished);
                return;
            }

            // A track that failed is not put back, or it would fail again on every round.
            if (m.Outcome != Outcome.Failed && _loop == LoopMode.Queue)
                _queue.Add([finished], playNext: false);
        }

        if (_loop == LoopMode.Track) _loop = LoopMode.Off;
        _current = null;

        if (_closing) FinishClose();
        else StartNext();
    }

    private void OnClose(CloseMessage m)
    {
        if (_closing) return;
        if (m.OnlyIfUnused && (_current is not null || _queue.Count > 0)) return;

        _closing = true;
        _logger.LogDebug("Closing player: {Reason}", m.Reason);

        _queue.Clear();
        _loop = LoopMode.Off;
        CancelIdle();
        _watchCts.Cancel();

        if (!m.FromDispose) RaiseCloseRequested();

        if (_trackCts is not null) _trackCts.Cancel(); // OnTrackEnded finishes the close
        else
        {
            _current = null;
            FinishClose();
        }
    }

    private void StartNext()
    {
        if (_closing) return;

        if (_queue.TryDequeue(out var next))
        {
            _current = next;
            StartTrack(next);
        }
        else
        {
            _current = null;
            _loop = LoopMode.Off;
            BeginIdle();
        }
    }

    private void StartTrack(MusicTrackNew track)
    {
        CancelIdle();
        _skipVotes.Clear();

        var cts = new CancellationTokenSource();
        _trackCts = cts;
        var generation = ++_generation;

        // Warm up the next track while this one plays.
        if (_queue.PeekNext() is { } upcoming) _source.PreFetch(upcoming);

        _ = Task.Run(() => RunTrackAsync(track, generation, cts.Token));
    }

    private void FinishClose() => _inbox.Writer.TryComplete();

    private void RaiseCloseRequested()
    {
        if (Interlocked.Exchange(ref _closeRaised, 1) != 0) return;

        try
        {
            _closeRequested?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Close request failed.");
        }
    }

    private void Publish() =>
        _snapshot = new PlayerSnapshot(_current, [.. _queue.Items], _loop, _skipVotes.Count, _closing);

    // Replies are sent only after the new state is published, so a caller that awaits a command and then reads
    // Snapshot always sees that command's effect.
    private void Complete<T>(TaskCompletionSource<T> reply, T value)
    {
        Publish();
        reply.TrySetResult(value);
    }

    private void Complete(TaskCompletionSource reply)
    {
        Publish();
        reply.TrySetResult();
    }

    // ---------------------------------------------------------------- playing one track (runs off the loop)

    private async Task RunTrackAsync(MusicTrackNew track, int generation, CancellationToken token)
    {
        var outcome = Outcome.Failed;

        try
        {
            await using var stream = await _source.ResolveStreamAsync(track);

            if (token.IsCancellationRequested)
            {
                outcome = Outcome.Cancelled; // skipped while still resolving
            }
            else
            {
                _metrics.TrackStarted();
                _logger.LogInformation("Now playing: {Title}", track.Title);

                await _player.PlayAsync(stream, token);
                outcome = token.IsCancellationRequested ? Outcome.Cancelled : Outcome.Completed;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _logger.LogDebug("Playback of '{Title}' was cancelled.", track.Title);
            outcome = Outcome.Cancelled;
        }
        catch (UserFacingException ex)
        {
            // Expected trouble with this particular track (unavailable, rate-limited, download failed...): tell the channel why.
            _metrics.Error("playback.unavailable");
            if (ex.InnerException is null) _logger.LogInformation("Cannot play '{Title}': {Reason}", track.Title, ex.Message);
            else _logger.LogWarning(ex.InnerException, "Cannot play '{Title}': {Reason}", track.Title, ex.Message);
            await _notifier.SendAsync($"Couldn't play **{track.Title}**: {ex.Message.TrimEnd('.')}.");
        }
        catch (HttpRequestException ex)
        {
            _metrics.Error("playback.http");
            _logger.LogWarning(ex, "HTTP request failed while playing '{Title}'.", track.Title);
            await _notifier.SendAsync($"Couldn't play **{track.Title}**: {FailureDescriber.Describe(ex)}.");
        }
        catch (AudioDecodingException ex)
        {
            // ffmpeg's own output is for the log, not for the channel.
            _metrics.Error("playback.decode");
            _logger.LogError(ex, "Decoding error while playing '{Title}'.", track.Title);
            await _notifier.SendAsync($"Couldn't play **{track.Title}**: I couldn't decode its audio.");
        }
        catch (Exception ex)
        {
            _metrics.Error("playback.unexpected");
            _logger.LogError(ex, "Unexpected error while playing '{Title}'.", track.Title);
            await _notifier.SendAsync($"Something went wrong while playing **{track.Title}**. It has been logged.");
        }

        Post(new TrackEndedMessage(generation, outcome));
    }

    // ---------------------------------------------------------------- idle / alone timers

    private void BeginIdle()
    {
        CancelIdle();
        if (_options.IdleTimeout <= TimeSpan.Zero) return;

        var cts = new CancellationTokenSource();
        _idleCts = cts;
        var generation = ++_idleGeneration;

        _ = Task.Delay(_options.IdleTimeout, _time, cts.Token).ContinueWith(
            t => { if (t.IsCompletedSuccessfully) Post(new IdleElapsedMessage(generation)); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void CancelIdle()
    {
        _idleGeneration++; // invalidates any timer message already in flight
        _idleCts?.Cancel();
        _idleCts?.Dispose();
        _idleCts = null;
    }

    private async Task WatchAsync(CancellationToken token)
    {
        if (_options.AloneCheckInterval <= TimeSpan.Zero) return;

        using var scope = _logger.BeginScope("guild={GuildId}", _guildId);
        DateTimeOffset? aloneSince = null;
        using var timer = new PeriodicTimer(_options.AloneCheckInterval, _time);

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                var listeners = _voice.ListenerCount();
                if (listeners is null) continue; // unknown right now (e.g. cache not populated): don't act on it

                if (listeners > 0)
                {
                    aloneSince = null;
                    continue;
                }

                aloneSince ??= _time.GetUtcNow();
                if (_time.GetUtcNow() - aloneSince < _options.AloneTimeout) continue;

                _logger.LogInformation("Nobody else is in the voice channel. Leaving.");
                Post(new CloseMessage("channel empty", false, false));
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Player closed.
        }
    }

    private void OnDisconnected(bool willReconnect)
    {
        // NetCord reconnects by itself for transient drops; only end the session when it won't.
        if (!willReconnect) Post(new CloseMessage("voice disconnected", false, false));
    }

    // ---------------------------------------------------------------- plumbing

    private bool Post(Message message) => _inbox.Writer.TryWrite(message);

    private Task<T> Ask<T>(Func<TaskCompletionSource<T>, Message> create)
    {
        var reply = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Post(create(reply))) reply.TrySetException(new SessionClosedException());
        return reply.Task;
    }

    private Task Ask(Func<TaskCompletionSource, Message> create)
    {
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Post(create(reply))) reply.TrySetException(new SessionClosedException());
        return reply.Task;
    }

    private static void Fail(Message message, Exception ex)
    {
        switch (message)
        {
            case EnqueueMessage m: m.Reply.TrySetException(ex); break;
            case SkipMessage m: m.Reply.TrySetException(ex); break;
            case ShuffleMessage m: m.Reply.TrySetException(ex); break;
            case LoopMessage m: m.Reply.TrySetException(ex); break;
            case RemoveMessage m: m.Reply.TrySetException(ex); break;
            case VoteMessage m: m.Reply.TrySetException(ex); break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _logger.LogDebug("Disposing player.");
        _voice.Disconnected -= OnDisconnected;
        Post(new CloseMessage("disposed", false, true));

        var stopped = true;
        try
        {
            await _loopTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            stopped = false;
            _logger.LogWarning("Player loop did not stop in time.");
        }

        _watchCts.Cancel();
        try
        {
            await _watchTask.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (TimeoutException)
        {
            // The watchdog only sleeps; leaving it is harmless.
        }

        await _voice.LeaveAsync();

        // If the loop is still unwinding, leave these for the GC rather than pull them out from under it.
        if (stopped)
        {
            CancelIdle();
            _watchCts.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
