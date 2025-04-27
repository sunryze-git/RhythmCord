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
    private GatewayClient? _gatewayClient;

    public GuildMusicService GetOrCreateManager(ApplicationCommandContext context)
    {
        _gatewayClient = gatewayClient;
        var guildId = context.Guild!.Id;
        var manager = _managers.GetOrAdd(guildId, _ => 
            serviceProvider.GetRequiredService<GuildMusicService>());
        
        manager.SetContextDependencies(context.Interaction.Channel);
        manager.PlaybackFinished += HandleClosingRunnerAsyncTask;
        logger.LogInformation("{GuildId} Guild music manager is ready.", guildId);
        return manager;
    }

    public void CloseManager(ulong guildId)
    {
        logger.LogInformation("Closing music manager for guild {GuildId}", guildId);
        if (!_managers.TryRemove(guildId, out var manager)) return;
        
        manager.PlaybackFinished -= HandleClosingRunnerAsyncTask;
        manager.Dispose();
    }

    public bool GuildIsActive(ulong guildId)
    {
        return _managers.ContainsKey(guildId);
    }

    private async Task HandleClosingRunnerAsyncTask(ulong guildId)
    {
        logger.LogInformation("Playback finished event received for Guild {GuildId}", guildId);

        try
        {
            await _gatewayClient!.UpdateVoiceStateAsync(new VoiceStateProperties(guildId, null));
            logger.LogInformation("Voice state updated for Guild {GuildId}", guildId);
        } catch (Exception e)
        {
            logger.LogError(e, "Failed to update voice state for Guild {GuildId}: {ErrorMessage}", guildId, e.Message);
        }
        
        CloseManager(guildId);
    }
}