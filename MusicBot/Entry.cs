using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MusicBot.Services;
using NetCord;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;
using Newtonsoft.Json;

namespace MusicBot;

public static class MusicBot
{
    private const string ConfigPath = "config.json";
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
        
        
        if (!File.Exists(ConfigPath))
        {
            _logger.LogCritical("No configuration found. Create a config.json and input a token.");
            return;
        }
        
        var config = JsonConvert.DeserializeObject<Parsers.Config>(File.ReadAllText(ConfigPath));
        if (config is null || string.IsNullOrEmpty(config.Token))
        {
            _logger.LogCritical("The configuration file was invalid or did not contain a token.");
            return;
        }

        // Startup the Client
        var client = new GatewayClient(new BotToken(config.Token));
        
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

        // Log out when interrupted via keyboard
        Console.CancelKeyPress += async delegate {
            _logger.LogInformation("Logging out!");
            await client.CloseAsync();
        };
        
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
