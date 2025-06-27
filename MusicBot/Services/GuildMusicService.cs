using Microsoft.Extensions.Logging;
using MusicBot.Services.Audio;
using MusicBot.Services.Media;
using MusicBot.Services.Media.Resolvers;
using MusicBot.Services.Queue;
using NetCord.Gateway;

namespace MusicBot.Services;

public class GuildMusicService
{
    public readonly PlaybackHandler PlaybackHandler;

    public GuildMusicService(
        ILoggerFactory loggerFactory,
        AudioServiceNative audioService,
        IEnumerable<IMediaResolver> resolvers,
        GatewayClient gatewayClient)
    {
        var queueManager = new QueueManager();
        
        var mediaResolver = new MediaResolver(
            loggerFactory.CreateLogger<MediaResolver>(),
            resolvers);
        
        PlaybackHandler = new PlaybackHandler(
            loggerFactory.CreateLogger<PlaybackHandler>(),
            audioService,
            queueManager,
            mediaResolver,
            gatewayClient);
    }
}
