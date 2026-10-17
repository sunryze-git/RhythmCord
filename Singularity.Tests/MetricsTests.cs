using Microsoft.Extensions.Logging;
using Singularity.Observability;

namespace Singularity.Tests;

public class MetricsTests
{
    [Fact]
    public void Counters_sum_and_break_down_by_their_tag()
    {
        using var metrics = new BotMetrics();
        metrics.Command("play");
        metrics.Command("play");
        metrics.Command("skip");

        Assert.Equal(3, metrics.Aggregates.Total(MetricNames.Commands));
        Assert.Equal(new Dictionary<string, long> { ["play"] = 2, ["skip"] = 1 }, metrics.Aggregates.ByTag(MetricNames.Commands));
    }

    [Fact]
    public void Plain_counters_work_without_tags()
    {
        using var metrics = new BotMetrics();
        metrics.TracksQueued(3);
        metrics.TracksQueued(2);
        metrics.CooldownHit();
        metrics.AudioBytes(1000);

        Assert.Equal(5, metrics.Aggregates.Total(MetricNames.TracksQueued));
        Assert.Equal(1, metrics.Aggregates.Total(MetricNames.CooldownHits));
        Assert.Equal(1000, metrics.Aggregates.Total(MetricNames.AudioBytes));
    }

    [Fact]
    public void Active_trackers_go_up_and_back_down_and_release_only_once()
    {
        using var metrics = new BotMetrics();

        var first = metrics.TrackFfmpeg();
        var second = metrics.TrackFfmpeg();
        Assert.Equal(2, metrics.Aggregates.Total(MetricNames.FfmpegActive));

        first.Dispose();
        first.Dispose(); // double dispose must not go negative
        Assert.Equal(1, metrics.Aggregates.Total(MetricNames.FfmpegActive));

        second.Dispose();
        Assert.Equal(0, metrics.Aggregates.Total(MetricNames.FfmpegActive));
    }

    [Fact]
    public void Histograms_report_a_count_and_a_recent_average()
    {
        using var metrics = new BotMetrics();
        Assert.Equal((0L, 0.0), metrics.Aggregates.Histogram(MetricNames.ResolveDuration));

        metrics.ResolveTime(100);
        Assert.Equal((1L, 100.0), metrics.Aggregates.Histogram(MetricNames.ResolveDuration));

        metrics.ResolveTime(200);
        var (count, average) = metrics.Aggregates.Histogram(MetricNames.ResolveDuration);
        Assert.Equal(2, count);
        Assert.InRange(average, 100, 200); // weighted toward the newest sample
    }

    [Fact]
    public void Observable_gauges_are_read_on_refresh()
    {
        using var metrics = new BotMetrics();
        var totals = new SessionTotals(3, 2, 17, 5, 1);
        metrics.ObserveSessions(() => totals);
        metrics.ObserveGuilds(() => 42);
        metrics.StartupMs = 640;
        metrics.GatewayConnected = true;

        metrics.Aggregates.Refresh();

        Assert.Equal(3, metrics.Aggregates.Gauge(MetricNames.VoiceSessions));
        Assert.Equal(2, metrics.Aggregates.Gauge(MetricNames.VoicePlaying));
        Assert.Equal(17, metrics.Aggregates.Gauge(MetricNames.VoiceQueued));
        Assert.Equal(5, metrics.Aggregates.Gauge(MetricNames.VoiceListeners));
        Assert.Equal(1, metrics.Aggregates.Gauge(MetricNames.VoiceLooping));
        Assert.Equal(42, metrics.Aggregates.Gauge(MetricNames.GatewayGuilds));
        Assert.Equal(640, metrics.Aggregates.Gauge(MetricNames.StartupDuration));
        Assert.Equal(1, metrics.Aggregates.Gauge(MetricNames.GatewayConnected));

        totals = new SessionTotals(0, 0, 0, 0, 0);
        metrics.GatewayConnected = false;
        metrics.Aggregates.Refresh();

        Assert.Equal(0, metrics.Aggregates.Gauge(MetricNames.VoiceSessions));
        Assert.Equal(0, metrics.Aggregates.Gauge(MetricNames.GatewayConnected));
    }

    [Fact]
    public void Separate_instances_do_not_see_each_others_measurements()
    {
        using var a = new BotMetrics();
        using var b = new BotMetrics();

        a.TrackStarted();

        Assert.Equal(1, a.Aggregates.Total(MetricNames.TracksStarted));
        Assert.Equal(0, b.Aggregates.Total(MetricNames.TracksStarted));
    }

    [Fact]
    public void Errors_are_counted_by_type()
    {
        using var metrics = new BotMetrics();
        metrics.Error("playback.decode");
        metrics.Error("playback.decode");
        metrics.Error("play.user");

        Assert.Equal(3, metrics.Aggregates.Total(MetricNames.Errors));
        Assert.Equal(2, metrics.Aggregates.ByTag(MetricNames.Errors)["playback.decode"]);
    }

    [Fact]
    public void Measurements_are_published_on_a_real_meter_other_tools_can_listen_to()
    {
        using var metrics = new BotMetrics();
        long seen = 0;
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == BotMetrics.MeterName && instrument.Name == MetricNames.TracksStarted) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref seen, value));
        listener.Start();

        metrics.TrackStarted();

        Assert.Equal(1, Interlocked.Read(ref seen));
    }

    [Fact]
    public void Warnings_and_errors_in_the_log_are_counted_by_level()
    {
        using var metrics = new BotMetrics();
        var buffer = new LogBuffer();
        using var provider = new BufferLoggerProvider(buffer, metrics);
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("fine");
        logger.LogWarning("hmm");
        logger.LogError("bad");
        logger.LogCritical("worse");

        var levels = metrics.Aggregates.ByTag(MetricNames.LogEntries);
        Assert.Equal(1, levels["warning"]);
        Assert.Equal(2, levels["error"]);
        Assert.Equal(4, buffer.Snapshot(10).Count);
    }
}
