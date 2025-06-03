using Microsoft.Extensions.Logging;
using CobaltApi;
using MusicBot.Services.Audio;
using MusicBot.Services.Media;
using MusicBot.Services.Queue;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;

namespace MusicBot.Services;

public class GuildMusicService
{
    public readonly PlaybackHandler PlaybackHandler;

    public GuildMusicService(
        ILoggerFactory loggerFactory,
        AudioService audioService,
        SearchService searchService,
        YoutubeService youtubeService,
        GatewayClient gatewayClient)
    {
        var cobaltClient = new CobaltClient("http://192.168.1.91:9000");
        
        var queueManager = new QueueManager();
        
        var mediaResolver = new MediaResolver(
            loggerFactory.CreateLogger<MediaResolver>(),
            searchService,
            youtubeService,
            cobaltClient);
        
        PlaybackHandler = new PlaybackHandler(
            loggerFactory.CreateLogger<PlaybackHandler>(),
            audioService,
            queueManager,
            mediaResolver,
            gatewayClient);
    }
}
