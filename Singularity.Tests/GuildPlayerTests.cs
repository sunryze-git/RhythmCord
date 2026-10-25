using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Singularity.Features.Music;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Player;
using Singularity.Features.Music.Services;
using Singularity.Infrastructure;
using Singularity.Observability;

namespace Singularity.Tests;

public sealed class GuildPlayerTests : IAsyncLifetime
{
    // ---------------------------------------------------------------- fakes

    private sealed class TitledStream(string title) : MemoryStream
    {
        public string Title { get; } = title;
    }

    private sealed class FakeSource : ITrackSource
    {
        public HashSet<string> Unresolvable { get; } = [];
        public Dictionary<string, Exception> StreamFailures { get; } = new();
        public List<string> PreFetched { get; } = [];

        public Task<IReadOnlyList<MusicTrackNew>> ResolveSongsAsync(string query) => Task.FromResult<IReadOnlyList<MusicTrackNew>>([]);

        public Task<Stream> ResolveStreamAsync(MusicTrackNew track)
        {
            if (StreamFailures.TryGetValue(track.Title, out var failure)) throw failure;
            if (Unresolvable.Contains(track.Title)) throw new StreamUnavailableException("the video is private");
            return Task.FromResult<Stream>(new TitledStream(track.Title));
        }

        public void PreFetch(MusicTrackNew track) => PreFetched.Add(track.Title);
    }

    /// <summary>"Plays" until the test says the track is over (or it is cancelled, which returns quietly like the real one).</summary>
    private sealed class FakePlayer : ITrackPlayer
    {
        private readonly Lock _lock = new();
        private TaskCompletionSource<bool>? _current;

        public List<string> Started { get; } = [];
        public HashSet<string> FailOn { get; } = [];
        public Exception? Failure { get; set; }
        public TimeSpan Position => TimeSpan.Zero;

        public async Task PlayAsync(Stream input, CancellationToken cancellationToken)
        {
            var title = ((TitledStream)input).Title;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                Started.Add(title);
                _current = tcs;
            }

            if (FailOn.Contains(title)) throw Failure ?? new InvalidOperationException("player exploded");

            using var registration = cancellationToken.Register(() => tcs.TrySetResult(false));
            await tcs.Task;
        }

        public void FinishCurrent()
        {
            lock (_lock) _current?.TrySetResult(true);
        }

        public string[] StartedSnapshot()
        {
            lock (_lock) return Started.ToArray();
        }
    }

    private sealed class FakeVoice : IVoiceSession
    {
        public event Action<bool>? Disconnected;
        public int? Listeners { get; set; } = 2;
        public int Left;

        public int? ListenerCount() => Listeners;

        public Task LeaveAsync()
        {
            Interlocked.Increment(ref Left);
            return Task.CompletedTask;
        }

        public void RaiseDisconnected(bool willReconnect) => Disconnected?.Invoke(willReconnect);
    }

    private sealed class FakeNotifier : IPlayerNotifier
    {
        public List<string> Messages { get; } = [];

        public Task SendAsync(string message)
        {
            lock (Messages) Messages.Add(message);
            return Task.CompletedTask;
        }

        public string[] Snapshot()
        {
            lock (Messages) return Messages.ToArray();
        }
    }

    // ---------------------------------------------------------------- harness

    private readonly FakeSource _source = new();
    private readonly FakePlayer _audio = new();
    private readonly FakeVoice _voice = new();
    private readonly FakeNotifier _notifier = new();
    private readonly BotMetrics _metrics = new();
    private readonly FakeTimeProvider _time = new();
    private int _closeRequests;
    private GuildPlayer _player = null!;

    private MusicOptions Options { get; set; } = new()
    {
        MaxQueueLength = 100,
        IdleTimeout = TimeSpan.FromMinutes(10),
        AloneCheckInterval = TimeSpan.FromSeconds(10),
        AloneTimeout = TimeSpan.FromSeconds(30)
    };

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_player is not null) await _player.DisposeAsync();
    }

    private GuildPlayer Start(MusicOptions? options = null)
    {
        _player = new GuildPlayer(_source, _audio, _voice, _notifier, _metrics, options ?? Options, _time, NullLogger<GuildPlayer>.Instance);
        _player.Initialize(1, () => Interlocked.Increment(ref _closeRequests));
        return _player;
    }

    private long Count(string metric) => _metrics.Aggregates.Total(metric);

    private long Errors(string type) => _metrics.Aggregates.ByTag(MetricNames.Errors).GetValueOrDefault(type);

    private static async Task WaitUntil(Func<bool> condition, string because)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "Timed out waiting for: " + because);
    }

    private async Task WaitForCurrent(string title) =>
        await WaitUntil(() => _player.Snapshot.Current?.Title == title && _audio.StartedSnapshot().Contains(title), $"'{title}' to be playing");

    private async Task EnqueueAsync(params string[] titles) =>
        await _player.EnqueueAsync(titles.Select(t => Tracks.Make(t)).ToList(), next: false, requestedBy: 7);

    // ---------------------------------------------------------------- tests

    [Fact]
    public async Task Enqueue_starts_the_first_track_and_queues_the_rest()
    {
        Start();

        var result = await _player.EnqueueAsync(Tracks.Many("t", 3), next: false, requestedBy: 7);
        await WaitForCurrent("t0");

        Assert.Equal(3, result.Accepted.Count);
        Assert.Equal(["t1", "t2"], _player.Snapshot.Pending.Select(t => t.Title));
        Assert.All(result.Accepted, t => Assert.Equal(7UL, t.RequestedBy));
        Assert.Equal(1, Count(MetricNames.TracksStarted));
        Assert.Equal(3, Count(MetricNames.TracksQueued));
    }

    [Fact]
    public async Task A_finished_track_moves_on_to_the_next_and_the_queue_runs_dry()
    {
        Start();
        await EnqueueAsync("a", "b");
        await WaitForCurrent("a");

        _audio.FinishCurrent();
        await WaitForCurrent("b");
        _audio.FinishCurrent();

        await WaitUntil(() => _player.Snapshot.Current is null, "the queue to run dry");
        Assert.Equal(["a", "b"], _audio.StartedSnapshot());
        Assert.Equal(0, Count(MetricNames.TracksSkipped));
    }

    [Fact]
    public async Task Skip_moves_on_and_is_counted()
    {
        Start();
        await EnqueueAsync("a", "b");
        await WaitForCurrent("a");

        await _player.SkipAsync();

        await WaitForCurrent("b");
        Assert.Equal(1, Count(MetricNames.TracksSkipped));
    }

    [Fact]
    public async Task Skip_for_a_track_that_is_no_longer_current_does_nothing()
    {
        Start();
        await EnqueueAsync("a", "b");
        await WaitForCurrent("a");
        var stale = _player.Snapshot.Current;
        _audio.FinishCurrent();
        await WaitForCurrent("b");

        await _player.SkipAsync(stale);
        await Task.Delay(100);

        Assert.Equal("b", _player.Snapshot.Current!.Title);
        Assert.Equal(0, Count(MetricNames.TracksSkipped));
    }

    [Fact]
    public async Task Stop_clears_the_queue_and_ends_the_current_track()
    {
        Start();
        await EnqueueAsync("a", "b", "c");
        await WaitForCurrent("a");

        await _player.StopAsync();

        await WaitUntil(() => _player.Snapshot.Current is null, "playback to stop");
        Assert.True(_player.Snapshot.Pending.IsEmpty);
        Assert.Equal(["a"], _audio.StartedSnapshot());
    }

    [Fact]
    public async Task Play_next_goes_after_the_current_track_but_before_the_rest()
    {
        Start();
        await EnqueueAsync("a", "b", "c");
        await WaitForCurrent("a");

        await _player.EnqueueAsync([Tracks.Make("n")], next: true, requestedBy: 1);

        Assert.Equal("a", _player.Snapshot.Current!.Title);
        Assert.Equal(["n", "b", "c"], _player.Snapshot.Pending.Select(t => t.Title));
        _audio.FinishCurrent();
        await WaitForCurrent("n");
    }

    [Fact]
    public async Task Shuffle_never_touches_the_current_track_and_loses_nothing()
    {
        Start();
        await _player.EnqueueAsync(Tracks.Many("t", 30), next: false, requestedBy: 1);
        await WaitForCurrent("t0");

        await _player.ShuffleAsync();

        Assert.Equal("t0", _player.Snapshot.Current!.Title);
        Assert.Equal(Enumerable.Range(1, 29).Select(i => $"t{i}").Order(), _player.Snapshot.Pending.Select(t => t.Title).Order());
    }

    [Fact]
    public async Task Looping_replays_the_same_track_until_turned_off()
    {
        Start();
        await EnqueueAsync("a", "b");
        await WaitForCurrent("a");

        Assert.Equal(LoopMode.Track, await _player.SetLoopAsync(LoopMode.Track));
        Assert.True(_player.Snapshot.Looping);

        _audio.FinishCurrent();
        await WaitUntil(() => _audio.StartedSnapshot().Count(t => t == "a") == 2, "'a' to play again");
        Assert.Equal("a", _player.Snapshot.Current!.Title);

        Assert.Equal(LoopMode.Off, await _player.SetLoopAsync(LoopMode.Off));
        _audio.FinishCurrent();
        await WaitForCurrent("b");
    }

    [Fact]
    public async Task Setting_no_loop_mode_moves_through_off_track_and_queue()
    {
        Start();
        await EnqueueAsync("a");
        await WaitForCurrent("a");

        Assert.Equal(LoopMode.Track, await _player.SetLoopAsync(null));
        Assert.Equal(LoopMode.Queue, await _player.SetLoopAsync(null));
        Assert.Equal(LoopMode.Off, await _player.SetLoopAsync(null));
    }

    [Fact]
    public async Task Queue_loop_puts_each_finished_track_at_the_back()
    {
        Start();
        await EnqueueAsync("a", "b", "c");
        await WaitForCurrent("a");
        await _player.SetLoopAsync(LoopMode.Queue);

        _audio.FinishCurrent();
        await WaitForCurrent("b");
        Assert.Equal(["c", "a"], _player.Snapshot.Pending.Select(t => t.Title));

        _audio.FinishCurrent();
        await WaitForCurrent("c");
        _audio.FinishCurrent();
        await WaitUntil(() => _audio.StartedSnapshot().Count(t => t == "a") == 2, "'a' to come around again");
        Assert.Equal(LoopMode.Queue, _player.Snapshot.Loop);
    }

    [Fact]
    public async Task Skipping_during_queue_loop_keeps_the_loop_and_the_skipped_track()
    {
        Start();
        await EnqueueAsync("a", "b");
        await WaitForCurrent("a");
        await _player.SetLoopAsync(LoopMode.Queue);

        await _player.SkipAsync();

        await WaitForCurrent("b");
        Assert.Equal(["a"], _player.Snapshot.Pending.Select(t => t.Title));
        Assert.Equal(LoopMode.Queue, _player.Snapshot.Loop);
    }

    [Fact]
    public async Task Queue_loop_does_not_put_back_a_track_that_failed()
    {
        Start();
        _audio.FailOn.Add("bad");
        await EnqueueAsync("a", "bad", "c");
        await WaitForCurrent("a");
        await _player.SetLoopAsync(LoopMode.Queue);

        _audio.FinishCurrent();
        await WaitForCurrent("c");

        Assert.DoesNotContain("bad", _player.Snapshot.Pending.Select(t => t.Title));
    }

    [Fact]
    public async Task Stopping_ends_a_queue_loop()
    {
        Start();
        await EnqueueAsync("a", "b");
        await WaitForCurrent("a");
        await _player.SetLoopAsync(LoopMode.Queue);

        await _player.StopAsync();

        await WaitUntil(() => _player.Snapshot.Current is null, "playback to stop");
        Assert.Empty(_player.Snapshot.Pending);
        Assert.Equal(LoopMode.Off, _player.Snapshot.Loop);
    }

    [Fact]
    public async Task Remove_takes_a_waiting_track_out_of_the_queue()
    {
        Start();
        await EnqueueAsync("a", "b", "c");
        await WaitForCurrent("a");
        var b = _player.Snapshot.Pending[0];

        Assert.True(await _player.RemoveAsync(b));
        Assert.Equal(["c"], _player.Snapshot.Pending.Select(t => t.Title));
        Assert.False(await _player.RemoveAsync(b));
    }

    [Fact]
    public async Task Remove_ignores_the_track_that_is_already_playing()
    {
        Start();
        await EnqueueAsync("a", "b");
        await WaitForCurrent("a");

        Assert.False(await _player.RemoveAsync(_player.Snapshot.Current!));
        Assert.Equal("a", _player.Snapshot.Current!.Title);
    }

    [Fact]
    public async Task Skipping_a_looping_track_ends_the_loop()
    {
        Start();
        await EnqueueAsync("a", "b");
        await WaitForCurrent("a");
        await _player.SetLoopAsync(LoopMode.Track);

        await _player.SkipAsync();

        await WaitForCurrent("b");
        Assert.False(_player.Snapshot.Looping);
    }

    [Fact]
    public async Task Setting_loop_with_nothing_playing_does_nothing()
    {
        Start();

        Assert.Null(await _player.SetLoopAsync(LoopMode.Track));
        Assert.False(_player.Snapshot.Looping);
    }

    [Fact]
    public async Task Skip_votes_count_distinct_users_and_reset_for_each_track()
    {
        Start();
        await EnqueueAsync("a", "b");
        await WaitForCurrent("a");
        var a = _player.Snapshot.Current;

        Assert.Equal(1, await _player.VoteSkipAsync(100, a));
        Assert.Equal(1, await _player.VoteSkipAsync(100, a)); // same person again
        Assert.Equal(2, await _player.VoteSkipAsync(200, a));

        _audio.FinishCurrent();
        await WaitForCurrent("b");
        Assert.Equal(0, _player.Snapshot.SkipVotes);
        Assert.Equal(-1, await _player.VoteSkipAsync(300, a)); // a vote for the old track
    }

    // ---------------------------------------------------------------- how failures are reported

    [Fact]
    public async Task A_track_that_cannot_be_opened_says_why_in_the_channel_and_the_queue_keeps_going()
    {
        _source.Unresolvable.Add("bad");
        Start();

        await EnqueueAsync("bad", "good");

        await WaitForCurrent("good");
        var message = Assert.Single(_notifier.Snapshot());
        Assert.Contains("**bad**", message);
        Assert.Contains("the video is private", message); // the real reason, not a generic apology
        Assert.Equal(1, Errors("playback.unavailable"));
    }

    [Fact]
    public async Task A_network_failure_is_described_without_leaking_the_raw_exception()
    {
        _source.StreamFailures["net"] = new HttpRequestException("secret internal detail", null, System.Net.HttpStatusCode.TooManyRequests);
        Start();

        await EnqueueAsync("net", "next");

        await WaitForCurrent("next");
        var message = Assert.Single(_notifier.Snapshot());
        Assert.Contains("rate-limiting", message);
        Assert.DoesNotContain("secret internal detail", message);
        Assert.Equal(1, Errors("playback.http"));
    }

    [Fact]
    public async Task A_decoding_failure_keeps_ffmpegs_output_out_of_the_channel()
    {
        _audio.FailOn.Add("garbled");
        _audio.Failure = new AudioDecodingException("FFmpeg process exited with code 1: pipe:0: Invalid data found when processing input");
        Start();

        await EnqueueAsync("garbled", "next");

        await WaitUntil(() => _notifier.Snapshot().Length == 1, "the decode message");
        var message = _notifier.Snapshot()[0];
        Assert.Contains("couldn't decode", message);
        Assert.DoesNotContain("Invalid data", message);
        Assert.Equal(1, Errors("playback.decode"));
    }

    [Fact]
    public async Task A_player_bug_gets_a_generic_message_and_never_shows_the_exception_text()
    {
        _audio.FailOn.Add("boom");
        _audio.Failure = new InvalidOperationException("secret stack detail");
        Start();

        await EnqueueAsync("boom", "fine");

        await WaitForCurrent("fine");
        var message = Assert.Single(_notifier.Snapshot());
        Assert.Contains("Something went wrong", message);
        Assert.DoesNotContain("secret stack detail", message);
        Assert.Equal(1, Errors("playback.unexpected"));
    }

    [Fact]
    public async Task Failures_are_not_counted_as_skips_and_do_not_stall_the_queue()
    {
        _source.Unresolvable.Add("bad");
        Start();

        await EnqueueAsync("bad", "good");
        await WaitForCurrent("good");

        Assert.Equal(0, Count(MetricNames.TracksSkipped));
        Assert.Equal(1, Count(MetricNames.TracksStarted)); // only "good" actually started
    }

    [Fact]
    public async Task A_failing_track_does_not_loop_forever()
    {
        _audio.FailOn.Add("boom");
        Start();
        await EnqueueAsync("boom");
        await WaitUntil(() => _player.Snapshot.Current is null && _audio.StartedSnapshot().Length == 1, "the failure");

        Assert.Single(_audio.StartedSnapshot());
    }

    [Fact]
    public async Task The_queue_limit_caps_what_is_accepted()
    {
        Start(new MusicOptions { MaxQueueLength = 3, IdleTimeout = TimeSpan.Zero });

        var result = await _player.EnqueueAsync(Tracks.Many("t", 10), next: false, requestedBy: 1);
        await WaitForCurrent("t0");

        Assert.Equal(3, result.Accepted.Count);
        Assert.Equal(2, _player.Snapshot.Pending.Count); // t0 is playing, two wait: one slot is free again

        var more = await _player.EnqueueAsync(Tracks.Many("x", 5), next: false, requestedBy: 1);

        Assert.Single(more.Accepted);
        Assert.Equal(3, _player.Snapshot.Pending.Count);
    }

    [Fact]
    public async Task The_next_track_is_prefetched_when_a_track_starts()
    {
        Start();

        await EnqueueAsync("a", "b");
        await WaitForCurrent("a");

        Assert.Contains("b", _source.PreFetched);
    }

    [Fact]
    public async Task Idle_timeout_asks_to_close_and_then_refuses_new_work()
    {
        Start();

        _time.Advance(TimeSpan.FromMinutes(11));
        await WaitUntil(() => _closeRequests == 1 && _player.Snapshot.Closing, "the idle close");

        await Assert.ThrowsAsync<SessionClosedException>(() => EnqueueAsync("late"));
    }

    [Fact]
    public async Task Enqueuing_restarts_the_idle_timer()
    {
        Start();
        _time.Advance(TimeSpan.FromMinutes(9));

        await EnqueueAsync("a");
        await WaitForCurrent("a");
        _audio.FinishCurrent();
        await WaitUntil(() => _player.Snapshot.Current is null, "the track to end");

        _time.Advance(TimeSpan.FromMinutes(9)); // 18 minutes since start, but only 9 since the track ended
        await Task.Delay(100);

        Assert.Equal(0, _closeRequests);
    }

    [Fact]
    public async Task Being_alone_in_the_channel_for_long_enough_closes_the_player()
    {
        _voice.Listeners = 0;
        Start();

        for (var i = 0; i < 12 && _closeRequests == 0; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(10));
            await Task.Delay(30);
        }

        await WaitUntil(() => _closeRequests == 1, "the empty-channel close");
    }

    [Fact]
    public async Task Someone_returning_resets_the_alone_timer_and_unknown_counts_are_ignored()
    {
        _voice.Listeners = 0;
        Start();

        for (var i = 0; i < 2; i++) { _time.Advance(TimeSpan.FromSeconds(10)); await Task.Delay(30); }
        _voice.Listeners = 1; // someone joined
        for (var i = 0; i < 2; i++) { _time.Advance(TimeSpan.FromSeconds(10)); await Task.Delay(30); }
        _voice.Listeners = null; // cache miss
        for (var i = 0; i < 8; i++) { _time.Advance(TimeSpan.FromSeconds(10)); await Task.Delay(30); }

        Assert.Equal(0, _closeRequests);
    }

    [Fact]
    public async Task A_dropped_voice_connection_closes_only_when_it_will_not_reconnect()
    {
        Start();

        _voice.RaiseDisconnected(willReconnect: true);
        await Task.Delay(100);
        Assert.Equal(0, _closeRequests);

        _voice.RaiseDisconnected(willReconnect: false);
        await WaitUntil(() => _closeRequests == 1, "the disconnect close");
    }

    [Fact]
    public async Task CloseIfUnused_closes_an_unused_player_but_not_one_that_is_playing()
    {
        Start();
        _player.CloseIfUnused();
        await WaitUntil(() => _closeRequests == 1, "the unused close");
    }

    [Fact]
    public async Task CloseIfUnused_leaves_a_playing_player_alone()
    {
        Start();
        await EnqueueAsync("a");
        await WaitForCurrent("a");

        _player.CloseIfUnused();
        await Task.Delay(100);

        Assert.Equal(0, _closeRequests);
        Assert.False(_player.Snapshot.Closing);
    }

    [Fact]
    public async Task Closing_stops_the_current_track_and_clears_the_queue()
    {
        Start();
        await EnqueueAsync("a", "b");
        await WaitForCurrent("a");

        _player.RequestClose();

        await WaitUntil(() => _player.Snapshot.Closing && _player.Snapshot.Current is null, "the close");
        Assert.Equal(["a"], _audio.StartedSnapshot());
        Assert.True(_player.Snapshot.Pending.IsEmpty);
        Assert.Equal(0, Count(MetricNames.TracksSkipped)); // ending because of a close is not a skip
    }

    [Fact]
    public async Task Dispose_stops_playback_leaves_voice_and_does_not_ask_to_close()
    {
        Start();
        await EnqueueAsync("a");
        await WaitForCurrent("a");

        await _player.DisposeAsync();

        Assert.Equal(1, _voice.Left);
        Assert.Equal(0, _closeRequests); // the owner is already disposing us
        await Assert.ThrowsAsync<SessionClosedException>(() => EnqueueAsync("late"));
    }

    [Fact]
    public async Task Dispose_is_safe_to_call_twice()
    {
        Start();

        await _player.DisposeAsync();
        await _player.DisposeAsync();

        Assert.Equal(1, _voice.Left);
    }

    [Fact]
    public async Task Concurrent_commands_never_corrupt_the_queue()
    {
        Start();
        await EnqueueAsync("first");
        await WaitForCurrent("first");

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            await _player.EnqueueAsync([Tracks.Make($"w{i}")], next: i % 2 == 0, requestedBy: (ulong)i);
            if (i % 5 == 0) await _player.ShuffleAsync();
        })));

        var snapshot = _player.Snapshot;
        Assert.Equal(20, snapshot.Pending.Count);
        Assert.Equal(20, snapshot.Pending.Select(t => t.Title).Distinct().Count());
    }
}
