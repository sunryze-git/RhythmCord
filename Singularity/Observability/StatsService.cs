using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Singularity.Features.Music;

namespace Singularity.Observability;

/// <summary>Builds the dashboard snapshot from the metrics instruments plus process counters.</summary>
internal sealed class StatsService(BotMetrics metrics, LogBuffer logBuffer, MusicOptions musicOptions)
{
    private static readonly string Version =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "dev";

    private readonly Lock _cpuLock = new();
    private TimeSpan _lastCpu = Environment.CpuUsage.TotalTime;
    private long _lastCpuStamp = Stopwatch.GetTimestamp();
    private double _cpuPercent;

    internal StatsSnapshot Snapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var m = metrics.Aggregates;
        m.Refresh(); // pull the observable gauges (voice sessions, guild count, ...)

        var errors = m.ByTag(MetricNames.Errors);
        var logs = m.ByTag(MetricNames.LogEntries);
        var resolve = m.Histogram(MetricNames.ResolveDuration);
        var stream = m.Histogram(MetricNames.StreamDuration);

        return new StatsSnapshot(
            Version,
            now,
            metrics.StartedAt,
            (long)(now - metrics.StartedAt).TotalSeconds,
            metrics.StartupMs,
            new GatewayStats(m.Gauge(MetricNames.GatewayConnected) == 1, (int)m.Gauge(MetricNames.GatewayGuilds), null),
            new VoiceStats(
                (int)m.Gauge(MetricNames.VoiceSessions),
                (int)m.Gauge(MetricNames.VoicePlaying),
                (int)m.Gauge(MetricNames.VoiceQueued),
                (int)m.Gauge(MetricNames.VoiceListeners),
                (int)m.Gauge(MetricNames.VoiceLooping)),
            new PlaybackStats(
                m.Total(MetricNames.TracksQueued),
                m.Total(MetricNames.TracksStarted),
                m.Total(MetricNames.TracksSkipped),
                errors.Where(e => e.Key.StartsWith("playback.", StringComparison.Ordinal)).Sum(e => e.Value),
                m.Total(MetricNames.SessionsOpened),
                Math.Round(m.Total(MetricNames.AudioBytes) / (48000.0 * 2 * 2) / 3600, 2),
                m.Total(MetricNames.FfmpegActive),
                m.Total(MetricNames.YtdlpActive),
                musicOptions.Normalize),
            new ResolverStats(
                m.Total(MetricNames.ResolveRequests),
                m.ByTag(MetricNames.Resolved),
                m.Total(MetricNames.ResolveNoResults),
                m.Total(MetricNames.StreamFallbacks),
                Math.Round(resolve.Average),
                Math.Round(stream.Average)),
            new CommandStats(m.Total(MetricNames.Commands), m.Total(MetricNames.CooldownHits), m.ByTag(MetricNames.Commands)),
            new LogStats(logs.GetValueOrDefault("warning"), logs.GetValueOrDefault("error")),
            new ErrorStats(
                m.Total(MetricNames.Errors),
                // ".user" (bad input, nothing found...) and ".refused" (failed preconditions) are normal; the rest are faults.
                errors.Where(e => !e.Key.EndsWith(".user", StringComparison.Ordinal) && !e.Key.EndsWith(".refused", StringComparison.Ordinal)).Sum(e => e.Value),
                errors),
            new ProcessStats(
                Math.Round(Environment.WorkingSet / 1048576.0, 1),
                Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1),
                SampleCpuPercent(),
                ThreadPool.ThreadCount,
                Environment.ProcessorCount,
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.OSDescription),
            logBuffer.Snapshot(150));
    }

    // CPU use as a percentage of one core, measured over the time since the last sample (at most once a second).
    private double SampleCpuPercent()
    {
        lock (_cpuLock)
        {
            var stamp = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(_lastCpuStamp, stamp);
            if (elapsed < TimeSpan.FromSeconds(1)) return _cpuPercent;

            var cpu = Environment.CpuUsage.TotalTime;
            _cpuPercent = Math.Round((cpu - _lastCpu).TotalSeconds / elapsed.TotalSeconds * 100, 1);
            _lastCpu = cpu;
            _lastCpuStamp = stamp;
            return _cpuPercent;
        }
    }
}
