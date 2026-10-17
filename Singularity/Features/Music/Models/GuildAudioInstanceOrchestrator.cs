using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Singularity.Features.Music.Player;
using Singularity.Observability;

namespace Singularity.Features.Music.Models;

public class GuildAudioInstanceOrchestrator
{
    private readonly ILogger<GuildAudioInstanceOrchestrator> logger;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly BotMetrics metrics;

    public GuildAudioInstanceOrchestrator(ILogger<GuildAudioInstanceOrchestrator> logger, IServiceScopeFactory scopeFactory, BotMetrics metrics)
    {
        this.logger = logger;
        this.scopeFactory = scopeFactory;
        this.metrics = metrics;
        metrics.ObserveSessions(() => GetAggregate().ToTotals());
    }


    // Lazy guarantees the factory (and therefore the DI scope) runs exactly once per guild.
    private readonly ConcurrentDictionary<ulong, Lazy<ManagerEntry>> _managers = new();

    internal GuildAudioInstance GetOrCreateManager(ulong guildId)
    {
        var lazy = _managers.GetOrAdd(guildId,
            id => new Lazy<ManagerEntry>(() => CreateEntry(id), LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return lazy.Value.Instance;
        }
        catch
        {
            // Don't leave a poisoned Lazy behind.
            _managers.TryRemove(new KeyValuePair<ulong, Lazy<ManagerEntry>>(guildId, lazy));
            throw;
        }
    }

    internal async ValueTask CloseManagerAsync(ulong guildId)
    {
        if (!_managers.TryGetValue(guildId, out var lazy)) return;
        logger.LogInformation("Voice session closed.");

        try
        {
            await lazy.Value.Scope.DisposeAsync();
        }
        catch (Exception ex)
        {
            // The session is going away regardless; whoever asked to close it can do nothing about this, but the operator should know.
            metrics.Error("session.dispose");
            logger.LogError(ex, "A voice session did not shut down cleanly.");
        }
        finally
        {
            // Only removed once fully torn down, so a quick /play after /leave can't race the old session's
            // voice disconnect.
            _managers.TryRemove(new KeyValuePair<ulong, Lazy<ManagerEntry>>(guildId, lazy));
        }
    }

    internal async ValueTask CloseAllManagersAsync()
    {
        foreach (var id in _managers.Keys.ToArray()) await CloseManagerAsync(id);
    }

    internal bool GuildIsActive(ulong guildId) => _managers.ContainsKey(guildId);

    /// <summary>Totals across all sessions. Deliberately contains no per-guild information.</summary>
    internal SessionAggregate GetAggregate()
    {
        int sessions = 0, playing = 0, queued = 0, listeners = 0, looping = 0;

        foreach (var lazy in _managers.Values)
        {
            if (!lazy.IsValueCreated) continue;

            var instance = lazy.Value.Instance;
            var snapshot = instance.Player.Snapshot;
            sessions++;
            if (snapshot.Current is not null) playing++;
            if (snapshot.Looping) looping++;
            queued += snapshot.Pending.Count;
            listeners += instance.Voice.ListenerCount() ?? 0;
        }

        return new SessionAggregate(sessions, playing, queued, listeners, looping);
    }

    // The player asks to be closed when it is idle, alone, disconnected or told to leave; closing disposes it.
    private async Task CloseInBackgroundAsync(ulong guildId)
    {
        try
        {
            await Task.Yield(); // never dispose a player from inside its own loop
            await CloseManagerAsync(guildId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to close the voice session.");
        }
    }

    private ManagerEntry CreateEntry(ulong guildId)
    {
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var instance = scope.ServiceProvider.GetRequiredService<GuildAudioInstance>();
            instance.Bind(guildId, () => _ = CloseInBackgroundAsync(guildId));
            logger.LogDebug("Created new GuildAudioInstance for guild {GuildId}.", guildId);
            return new ManagerEntry(instance, scope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
    private record ManagerEntry(GuildAudioInstance Instance, AsyncServiceScope Scope);
}

/// <summary>Aggregate session totals (no per-guild data).</summary>
internal readonly record struct SessionAggregate(int Sessions, int Playing, int Queued, int Listeners, int Looping)
{
    internal SessionTotals ToTotals() => new(Sessions, Playing, Queued, Listeners, Looping);
}
