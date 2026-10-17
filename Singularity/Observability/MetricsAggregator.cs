using System.Diagnostics.Metrics;

namespace Singularity.Observability;

/// <summary>
///     Listens to one <see cref="Meter" /> and keeps running totals the dashboard can read: counter sums (also per
///     first tag), gauge values, and a recent average for histograms. It is just a consumer of the public instruments.
/// </summary>
internal sealed class MetricsAggregator : IDisposable
{
    private const double HistogramAlpha = 0.2;

    private readonly MeterListener _listener = new();
    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _sums = new();
    private readonly Dictionary<string, Dictionary<string, long>> _byTag = new();
    private readonly Dictionary<string, double> _gauges = new();
    private readonly Dictionary<string, (long Count, double Average)> _histograms = new();

    internal MetricsAggregator(Meter meter)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, meter)) listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>(OnMeasurement);
        _listener.SetMeasurementEventCallback<int>(OnMeasurement);
        _listener.SetMeasurementEventCallback<double>(OnMeasurement);
        _listener.Start();
    }

    /// <summary>Pulls the current value of every observable gauge. Call before reading gauges.</summary>
    internal void Refresh() => _listener.RecordObservableInstruments();

    /// <summary>Sum of a counter (or up/down counter) across all tags.</summary>
    internal long Total(string name)
    {
        lock (_lock) return _sums.GetValueOrDefault(name);
    }

    /// <summary>A counter broken down by the value of its first tag.</summary>
    internal Dictionary<string, long> ByTag(string name)
    {
        lock (_lock)
            return _byTag.TryGetValue(name, out var tags)
                ? tags.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value)
                : new Dictionary<string, long>();
    }

    internal long Gauge(string name)
    {
        lock (_lock) return (long)_gauges.GetValueOrDefault(name);
    }

    /// <summary>Number of samples and the recent (exponentially weighted) average of a histogram.</summary>
    internal (long Count, double Average) Histogram(string name)
    {
        lock (_lock) return _histograms.GetValueOrDefault(name);
    }

    private void OnMeasurement<T>(Instrument instrument, T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        where T : struct
    {
        var value = Convert.ToDouble(measurement);
        var tag = tags.Length > 0 ? tags[0].Value?.ToString() ?? "" : null;
        var kind = instrument.GetType().GetGenericTypeDefinition();

        lock (_lock)
        {
            if (kind == typeof(Counter<>) || kind == typeof(UpDownCounter<>))
            {
                var amount = (long)value;
                _sums[instrument.Name] = _sums.GetValueOrDefault(instrument.Name) + amount;

                if (tag is not null)
                {
                    if (!_byTag.TryGetValue(instrument.Name, out var tagged))
                        _byTag[instrument.Name] = tagged = new Dictionary<string, long>();
                    tagged[tag] = tagged.GetValueOrDefault(tag) + amount;
                }
            }
            else if (kind == typeof(Histogram<>))
            {
                var (count, average) = _histograms.GetValueOrDefault(instrument.Name);
                _histograms[instrument.Name] = (count + 1, count == 0 ? value : average + HistogramAlpha * (value - average));
            }
            else
            {
                _gauges[instrument.Name] = value; // observable gauge
            }
        }
    }

    public void Dispose() => _listener.Dispose();
}
