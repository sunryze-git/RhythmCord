using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord.Services.ApplicationCommands;

namespace Singularity.Features.Music.Models;

public class GuildAudioInstanceOrchestrator(ILogger<GuildAudioInstanceOrchestrator> logger, IServiceScopeFactory scopeFactory)
{
    private readonly ConcurrentDictionary<ulong, ManagerEntry> _managers = new();
    internal int NumberOfActiveManagers => _managers.Count;

    internal GuildAudioInstance GetOrCreateManager(ApplicationCommandContext context)
    {
        var guildId = context.Guild!.Id;
        var entry = _managers.GetOrAdd(guildId, _ =>
        {
            var scope = scopeFactory.CreateAsyncScope();

            var instance = scope.ServiceProvider.GetRequiredService<GuildAudioInstance>();
            instance.Initialize(context);

            logger.LogInformation("Created new GuildAudioInstance for guild {GuildId}.", guildId);
            return new ManagerEntry(instance, scope);
        });

        return entry.Instance;
    }

    internal async ValueTask CloseManagerAsync(ulong guildId)
    {
        if (!_managers.TryRemove(guildId, out var entry)) return;
        logger.LogInformation("Closing manager for guild {GuildId}.", guildId);

        await entry.Scope.DisposeAsync();
    }

    internal async ValueTask CloseAllManagersAsync()
    {
        foreach (var id in _managers.Keys) await CloseManagerAsync(id);
    }

    internal bool GuildIsActive(ulong guildId) => _managers.ContainsKey(guildId);
    internal IEnumerable<GuildAudioInstance> GetActiveManagers() => _managers.Values.Select(x => x.Instance);

    private record ManagerEntry(GuildAudioInstance Instance, AsyncServiceScope Scope);
}
