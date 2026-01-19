using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NetCord.Services.ApplicationCommands;

namespace MusicBot.Features;

public class GuildAudioInstanceOrchestrator(
    ILogger<GuildAudioInstanceOrchestrator> logger,
    IServiceScopeFactory scopeFactory)
{
    private readonly ConcurrentDictionary<ulong, ManagerEntry> _managers = new();
    public int NumberOfActiveManagers => _managers.Count;

    public GuildAudioInstance GetOrCreateManager(ApplicationCommandContext context)
    {
        var guildId = context.Guild!.Id;
        var entry = _managers.GetOrAdd(guildId, _ =>
        {
            var scope = scopeFactory.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<Program.GuildAudioInstanceFactory>();
            var instance = factory(context);
            logger.LogInformation("Created new GuildAudioInstance for guild {GuildId}.", guildId);
            return new ManagerEntry(instance, scope);
        });

        return entry.Instance;
    }

    public void CloseManager(ulong guildId)
    {
        if (!_managers.TryRemove(guildId, out var entry)) return;
        logger.LogInformation("Closing manager for guild {GuildId}.", guildId);
        entry.Scope.Dispose();
    }

    public void CloseAllManagers()
    {
        foreach (var id in _managers.Keys) CloseManager(id);
    }

    public bool GuildIsActive(ulong guildId) => _managers.ContainsKey(guildId);
    public IEnumerable<GuildAudioInstance> GetActiveManagers() => _managers.Values.Select(x => x.Instance);

    private record ManagerEntry(GuildAudioInstance Instance, IServiceScope Scope);
}
