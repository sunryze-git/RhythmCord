using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Singularity.Observability;

namespace Singularity.Features.Music.Player;

/// <summary>Posts player problems to the text channel where /play was last used.</summary>
internal sealed class ChannelNotifier(BotMetrics metrics, ILogger<ChannelNotifier> logger) : IPlayerNotifier
{
    private GatewayClient? _client;
    private long _channelId;

    internal void Update(ApplicationCommandContext context)
    {
        _client = context.Client;
        Interlocked.Exchange(ref _channelId, (long)context.Channel.Id);
    }

    public async Task SendAsync(string message)
    {
        var client = _client;
        var channelId = (ulong)Interlocked.Read(ref _channelId);
        if (client is null || channelId == 0) return;

        try
        {
            // Track titles are user-controlled text: never let them ping anyone.
            await client.Rest.SendMessageAsync(channelId,
                new MessageProperties { Content = message, AllowedMentions = AllowedMentionsProperties.None });
        }
        catch (Exception ex)
        {
            // Most often missing "Send Messages" permission. The user never sees the message, so the operator must.
            metrics.Error("notify.failed");
            logger.LogWarning(ex, "Could not post a playback message in the text channel (missing permissions?): {Message}", message);
        }
    }
}
