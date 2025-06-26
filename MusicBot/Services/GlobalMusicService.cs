using System.Collections.Concurrent;
using System.Reflection.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;

namespace MusicBot.Services;

public class GlobalMusicService(ILogger<GlobalMusicService> logger, IServiceProvider serviceProvider, GatewayClient gatewayClient)
{
    private readonly ConcurrentDictionary<ulong, GuildMusicService> _managers = new();

    public int NumberOfActiveManagers => _managers.Count;
    
    public GuildMusicService GetOrCreateManager(ApplicationCommandContext context)
    {
        var guildId = context.Guild!.Id;
        var manager = _managers.GetOrAdd(guildId, _ => 
            serviceProvider.GetRequiredService<GuildMusicService>());
        
        logger.LogInformation("{GuildId} Guild music manager is ready.", guildId);
        return manager;
    }

    public void CloseManager(ulong guildId)
    {
        logger.LogInformation("Closing music manager for guild {GuildId}", guildId);
        _managers.TryRemove(guildId, out _);
    }

    public bool GuildIsActive(ulong guildId)
    {
        return _managers.ContainsKey(guildId);
    }
}