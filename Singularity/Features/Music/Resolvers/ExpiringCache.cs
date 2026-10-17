using System.Collections.Concurrent;

namespace Singularity.Features.Music.Resolvers;

/// <summary>Small thread-safe cache whose entries stop being served after a fixed lifetime.</summary>
internal sealed class ExpiringCache<TValue>(TimeSpan lifetime, TimeProvider time, int pruneThreshold = 256)
    where TValue : class
{
    private readonly ConcurrentDictionary<string, (TValue Value, long Stamp)> _items = new();

    internal int Count => _items.Count;

    internal void Set(string key, TValue value)
    {
        if (_items.Count >= pruneThreshold) Prune();
        _items[key] = (value, time.GetTimestamp());
    }

    internal bool TryGetFresh(string key, out TValue value)
    {
        if (_items.TryGetValue(key, out var entry) && time.GetElapsedTime(entry.Stamp) < lifetime)
        {
            value = entry.Value;
            return true;
        }

        value = null!;
        return false;
    }

    internal void Remove(string key) => _items.TryRemove(key, out _);

    private void Prune()
    {
        foreach (var (key, entry) in _items)
            if (time.GetElapsedTime(entry.Stamp) >= lifetime)
                _items.TryRemove(key, out _);
    }
}
