using System.Text.Json.Serialization;

namespace Singularity.Observability;

// The JSON shape served at /api/stats. Aggregate numbers only: nothing in here may identify a guild, channel or user.

internal sealed record StatsSnapshot(
    string Version,
    DateTimeOffset Now,
    DateTimeOffset StartedAt,
    long UptimeSeconds,
    long StartupMs,
    GatewayStats Gateway,
    VoiceStats Voice,
    PlaybackStats Playback,
    ResolverStats Resolver,
    CommandStats Commands,
    LogStats Logs,
    ErrorStats Errors,
    ProcessStats Process,
    IReadOnlyList<LogLine> Recent);

internal sealed record GatewayStats(bool Connected, int Guilds, int? LatencyMs);

internal sealed record VoiceStats(int Sessions, int Playing, int Queued, int Listeners, int Looping);

internal sealed record PlaybackStats(
    long TracksQueued,
    long TracksStarted,
    long TracksSkipped,
    long Errors,
    long SessionsOpened,
    double AudioHours,
    long FfmpegActive,
    long YtdlpActive,
    bool Normalization);

internal sealed record ResolverStats(
    long Requests,
    Dictionary<string, long> BySource,
    long NoResults,
    long StreamFallbacks,
    double AvgResolveMs,
    double AvgStreamMs);

internal sealed record CommandStats(long Total, long CooldownHits, Dictionary<string, long> ByName);

internal sealed record LogStats(long Warnings, long Errors);

/// <summary>Errors by "area.kind" type (e.g. "playback.decode", "play.user", "unhandled.task").</summary>
internal sealed record ErrorStats(long Total, long Unexpected, Dictionary<string, long> ByType);

internal sealed record ProcessStats(
    double WorkingSetMb,
    double GcHeapMb,
    double CpuPercent,
    int Threads,
    int Cores,
    string Runtime,
    string Os);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StatsSnapshot))]
internal partial class DashboardJsonContext : JsonSerializerContext;
