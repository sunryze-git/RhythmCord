using System.Diagnostics;

namespace Singularity.Infrastructure;

/// <summary>Per-user, per-command cooldown to keep one person from flooding the bot.</summary>
public sealed class CommandCooldown
{
    private const int PruneThreshold = 2048;
    private static readonly TimeSpan MaxEntryAge = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new();
    private readonly Dictionary<(string Command, ulong UserId), long> _last = new();

    /// <summary>Returns true and records the use if the cooldown has elapsed; otherwise reports the time left.</summary>
    public bool TryAcquire(string command, ulong userId, TimeSpan cooldown, out TimeSpan remaining)
    {
        var now = Stopwatch.GetTimestamp();

        lock (_lock)
        {
            if (_last.Count > PruneThreshold)
            {
                foreach (var key in _last.Where(kv => Stopwatch.GetElapsedTime(kv.Value, now) > MaxEntryAge)
                             .Select(kv => kv.Key).ToList())
                    _last.Remove(key);
            }

            if (_last.TryGetValue((command, userId), out var previous))
            {
                var elapsed = Stopwatch.GetElapsedTime(previous, now);
                if (elapsed < cooldown)
                {
                    remaining = cooldown - elapsed;
                    return false;
                }
            }

            _last[(command, userId)] = now;
            remaining = TimeSpan.Zero;
            return true;
        }
    }
}
