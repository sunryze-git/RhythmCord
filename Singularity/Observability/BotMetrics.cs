using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Singularity.Observability;

/// <summary>Instrument names. Standard <c>System.Diagnostics.Metrics</c> names, so dotnet-counters / OpenTelemetry can read them too.</summary>
public static class MetricNames
{
    public const string Commands = "singularity.commands";
    public const string CooldownHits = "singularity.cooldown.hits";
    public const string TracksQueued = "singularity.tracks.queued";
    public const string TracksStarted = "singularity.tracks.started";
    public const string TracksSkipped = "singularity.tracks.skipped";
    public const string SessionsOpened = "singularity.sessions.opened";
    public const string AudioBytes = "singularity.audio.bytes";
    public const string ResolveRequests = "singularity.resolve.requests";
    public const string Resolved = "singularity.resolve.resolved";
    public const string ResolveNoResults = "singularity.resolve.no_results";
    public const string StreamFallbacks = "singularity.stream.fallbacks";
    public const string ResolveDuration = "singularity.resolve.duration";
    public const string StreamDuration = "singularity.stream.duration";
    public const string FfmpegActive = "singularity.ffmpeg.active";
    public const string YtdlpActive = "singularity.ytdlp.active";
    public const string LogEntries = "singularity.log.entries";
    public const string Errors = "singularity.errors";
    public const string BooruRequests = "singularity.booru.requests";

    public const string VoiceSessions = "singularity.voice.sessions";
    public const string VoicePlaying = "singularity.voice.playing";
    public const string VoiceQueued = "singularity.voice.queued";
    public const string VoiceListeners = "singularity.voice.listeners";
    public const string VoiceLooping = "singularity.voice.looping";
    public const string GatewayConnected = "singularity.gateway.connected";
    public const string GatewayGuilds = "singularity.gateway.guilds";
    public const string StartupDuration = "singularity.startup.duration";
}

/// <summary>Voice-session totals reported to the metrics system. Aggregate only: no guild is identifiable.</summary>
public readonly record struct SessionTotals(int Sessions, int Playing, int Queued, int Listeners, int Looping);

/// <summary>
///     The bot's measurements, published as <c>System.Diagnostics.Metrics</c> instruments on the "Singularity" meter.
///     Nothing recorded here may identify a guild, channel or user: only totals, and low-cardinality tags (command
///     name, resolver name, error type). The dashboard reads the same instruments through <see cref="Aggregates" />.
/// </summary>
public sealed class BotMetrics : IDisposable
{
    public const string MeterName = "Singularity";

    private readonly Meter _meter = new(MeterName, "1.0.0");

    private readonly Counter<long> _commands;
    private readonly Counter<long> _cooldownHits;
    private readonly Counter<long> _tracksQueued;
    private readonly Counter<long> _tracksStarted;
    private readonly Counter<long> _tracksSkipped;
    private readonly Counter<long> _sessionsOpened;
    private readonly Counter<long> _audioBytes;
    private readonly Counter<long> _resolveRequests;
    private readonly Counter<long> _resolved;
    private readonly Counter<long> _resolveNoResults;
    private readonly Counter<long> _streamFallbacks;
    private readonly Counter<long> _logEntries;
    private readonly Counter<long> _errors;
    private readonly Counter<long> _booruRequests;
    private readonly UpDownCounter<long> _ffmpegActive;
    private readonly UpDownCounter<long> _ytdlpActive;
    private readonly Histogram<double> _resolveDuration;
    private readonly Histogram<double> _streamDuration;

    private long _startupMs;
    private int _gatewayConnected;

    public BotMetrics()
    {
        _commands = _meter.CreateCounter<long>(MetricNames.Commands, "{command}", "Slash commands used, by command name.");
        _cooldownHits = _meter.CreateCounter<long>(MetricNames.CooldownHits, "{hit}", "Commands refused by a per-user cooldown.");
        _tracksQueued = _meter.CreateCounter<long>(MetricNames.TracksQueued, "{track}", "Tracks added to a queue.");
        _tracksStarted = _meter.CreateCounter<long>(MetricNames.TracksStarted, "{track}", "Tracks that started playing.");
        _tracksSkipped = _meter.CreateCounter<long>(MetricNames.TracksSkipped, "{track}", "Tracks ended early by a skip or stop.");
        _sessionsOpened = _meter.CreateCounter<long>(MetricNames.SessionsOpened, "{session}", "Voice sessions opened.");
        _audioBytes = _meter.CreateCounter<long>(MetricNames.AudioBytes, "By", "Decoded PCM bytes streamed to Discord.");
        _resolveRequests = _meter.CreateCounter<long>(MetricNames.ResolveRequests, "{request}", "Lookups requested.");
        _resolved = _meter.CreateCounter<long>(MetricNames.Resolved, "{request}", "Lookups answered, by resolver.");
        _resolveNoResults = _meter.CreateCounter<long>(MetricNames.ResolveNoResults, "{request}", "Lookups that found nothing.");
        _streamFallbacks = _meter.CreateCounter<long>(MetricNames.StreamFallbacks, "{fallback}", "Streams that needed a fallback resolver.");
        _logEntries = _meter.CreateCounter<long>(MetricNames.LogEntries, "{entry}", "Warning and error log entries, by level.");
        _errors = _meter.CreateCounter<long>(MetricNames.Errors, "{error}", "Handled and unhandled errors, by type.");
        _booruRequests = _meter.CreateCounter<long>(MetricNames.BooruRequests, "{request}", "e621 searches, by outcome.");
        _ffmpegActive = _meter.CreateUpDownCounter<long>(MetricNames.FfmpegActive, "{process}", "ffmpeg processes running now.");
        _ytdlpActive = _meter.CreateUpDownCounter<long>(MetricNames.YtdlpActive, "{process}", "yt-dlp processes running now.");
        _resolveDuration = _meter.CreateHistogram<double>(MetricNames.ResolveDuration, "ms", "Time to answer a lookup.");
        _streamDuration = _meter.CreateHistogram<double>(MetricNames.StreamDuration, "ms", "Time to open a track's audio stream.");

        _meter.CreateObservableGauge(MetricNames.StartupDuration, () => Interlocked.Read(ref _startupMs), "ms", "Time from process start to ready.");
        _meter.CreateObservableGauge(MetricNames.GatewayConnected, () => Volatile.Read(ref _gatewayConnected), "{connection}", "1 while connected to Discord.");

        // Created last: the listener must see every instrument, and starts before anything is recorded.
        Aggregates = new MetricsAggregator(_meter);
    }

    internal MetricsAggregator Aggregates { get; }

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public long StartupMs
    {
        get => Interlocked.Read(ref _startupMs);
        set => Interlocked.Exchange(ref _startupMs, value);
    }

    public bool GatewayConnected
    {
        get => Volatile.Read(ref _gatewayConnected) == 1;
        set => Volatile.Write(ref _gatewayConnected, value ? 1 : 0);
    }

    // ---- gauges that need a live view of something else

    public void ObserveSessions(Func<SessionTotals> provider)
    {
        _meter.CreateObservableGauge(MetricNames.VoiceSessions, () => provider().Sessions, "{session}", "Open voice sessions.");
        _meter.CreateObservableGauge(MetricNames.VoicePlaying, () => provider().Playing, "{session}", "Sessions currently playing.");
        _meter.CreateObservableGauge(MetricNames.VoiceQueued, () => provider().Queued, "{track}", "Tracks waiting across all queues.");
        _meter.CreateObservableGauge(MetricNames.VoiceListeners, () => provider().Listeners, "{listener}", "People listening across all sessions.");
        _meter.CreateObservableGauge(MetricNames.VoiceLooping, () => provider().Looping, "{session}", "Sessions with loop on.");
    }

    public void ObserveGuilds(Func<int> provider) =>
        _meter.CreateObservableGauge(MetricNames.GatewayGuilds, provider, "{guild}", "Servers the bot is in (a count only).");

    // ---- recording

    public void Command(string name) => _commands.Add(1, new KeyValuePair<string, object?>("command", name));
    public void CooldownHit() => _cooldownHits.Add(1);
    public void TracksQueued(int count) => _tracksQueued.Add(count);
    public void TrackStarted() => _tracksStarted.Add(1);
    public void TrackSkipped() => _tracksSkipped.Add(1);
    public void SessionOpened() => _sessionsOpened.Add(1);
    public void AudioBytes(long bytes) => _audioBytes.Add(bytes);

    public void ResolveRequested() => _resolveRequests.Add(1);
    public void ResolvedBy(string resolver) => _resolved.Add(1, new KeyValuePair<string, object?>("resolver", resolver));
    public void ResolveNoResults() => _resolveNoResults.Add(1);
    public void StreamFallback() => _streamFallbacks.Add(1);
    public void ResolveTime(double milliseconds) => _resolveDuration.Record(milliseconds);
    public void StreamTime(double milliseconds) => _streamDuration.Record(milliseconds);

    public void LogEntry(string level) => _logEntries.Add(1, new KeyValuePair<string, object?>("level", level));

    /// <summary>Counts an error. <paramref name="type" /> is "area.kind", e.g. "playback.decode" or "e621.user".</summary>
    public void Error(string type) => _errors.Add(1, new KeyValuePair<string, object?>("type", type));

    /// <summary>One e621 search: outcome is "ok", "empty" or "error".</summary>
    public void BooruRequest(string outcome) => _booruRequests.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>Counts an ffmpeg process as running until the returned token is disposed.</summary>
    public IDisposable TrackFfmpeg() => Track(_ffmpegActive);

    public IDisposable TrackYtdlp() => Track(_ytdlpActive);

    private static ActiveToken Track(UpDownCounter<long> counter)
    {
        counter.Add(1);
        return new ActiveToken(counter);
    }

    public void Dispose()
    {
        Aggregates.Dispose();
        _meter.Dispose();
    }

    private sealed class ActiveToken(UpDownCounter<long> counter) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) counter.Add(-1);
        }
    }
}
