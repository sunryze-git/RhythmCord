using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MusicBot.Services;
using NetCord;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;

namespace MusicBot;

public static class MusicBot
{
    private static ILogger? _logger;
    public static IServiceProvider? Services;
    
    public static async Task Main()
    {
        // Initialize Logger
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddFilter("Microsoft", LogLevel.Warning)
                .AddFilter("System", LogLevel.Warning)
                .AddFilter("Discord", LogLevel.Information)
                .AddFilter("MusicBot", LogLevel.Debug)
                .AddSimpleConsole(options =>
                {
                    options.IncludeScopes = true;
                    options.SingleLine = false;
                    options.TimestampFormat = "[MM/dd/yyyy HH:mm:ss]";
                });
        });
        
        _logger = loggerFactory.CreateLogger("MusicBot");
        _logger.LogInformation("Logger configured.");
        
        // Get Environment Token
        var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogCritical("No Discord token was found. Set the DISCORD_TOKEN environment variable.");
            return;
        }

        // Startup the Client
        var client = new GatewayClient(new BotToken(token));
        
        // Initialize the Service Collection, and load the client and application command service
        Services = new ServiceCollection()
            .AddSingleton(loggerFactory)
            .AddSingleton(client)
            .AddLogging()
            .AddSingleton<ApplicationCommandService<ApplicationCommandContext>>()
            .AddSingleton<InteractionService>()
            .AddSingleton<GlobalMusicService>()
            .AddSingleton<YoutubeService>()
            .AddTransient<GuildMusicService>()
            .AddTransient<AudioService>()
            .BuildServiceProvider();
        
        // Initialize the application command service
        var applicationCommandService = Services.GetRequiredService<ApplicationCommandService<ApplicationCommandContext>>();
        applicationCommandService.AddModules(typeof(MusicBot).Assembly);
        
        // Initialize the interaction service
        var interactionService = Services.GetRequiredService<InteractionService>();
        
        client.Log += DiscordClient_Log;
        client.InteractionCreate += interactionService.OnClientOnInteractionCreate;
        
        await applicationCommandService.CreateCommandsAsync(client.Rest, client.Id, true);
        await client.StartAsync();
        await Task.Delay(Timeout.Infinite);
    }

    private static ValueTask DiscordClient_Log(LogMessage logMessage)
    {
        var logLevel = logMessage.Severity switch
        {
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Info => LogLevel.Information,
            _ => LogLevel.None,
        };

        _logger?.Log(logLevel, logMessage.Exception, "{Message}", logMessage.Message);
        return ValueTask.CompletedTask;
    }
}
