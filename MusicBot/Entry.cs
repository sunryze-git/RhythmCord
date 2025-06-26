using FFmpeg.AutoGen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MusicBot.Services;
using MusicBot.Services.Audio;
using MusicBot.Services.Interactions;
using MusicBot.Services.Media;
using NetCord;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;
using Newtonsoft.Json;

namespace MusicBot;

public static class MusicBot
{
    private const string ConfigPath = "config.json";
    private static ILogger? _logger;
    internal static IServiceProvider? Services;
    
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
                    options.SingleLine = true;
                    options.TimestampFormat = "[MM/dd/yyyy HH:mm:ss.fffffff]";
                });
        });
        
        _logger = loggerFactory.CreateLogger("MusicBot");
        _logger.LogInformation("Logger configured.");
        
        
        if (!File.Exists(ConfigPath))
        {
            _logger.LogCritical("No configuration found. Create a config.json and input a token.");
            await Task.Delay(1000);
            return;
        }
        
        var config = JsonConvert.DeserializeObject<Parsers.Config>(await File.ReadAllTextAsync(ConfigPath));
        if (config is null || string.IsNullOrEmpty(config.Token))
        {
            _logger.LogCritical("The configuration file was invalid or did not contain a token.");
            await Task.Delay(1000);
            return;
        }

        // Startup the Client
        var client = new GatewayClient(new BotToken(config.Token), new GatewayClientConfiguration
        {
            Intents = GatewayIntents.AllNonPrivileged
        });
        
        // Set audio client root libs
        ffmpeg.RootPath = "/usr/lib";
        
        // Initialize the Service Collection, and load the client and application command service
        Services = new ServiceCollection()
            .AddSingleton(loggerFactory)
            .AddSingleton(client)
            .AddLogging()
            .AddSingleton<ApplicationCommandService<ApplicationCommandContext>>()
            .AddSingleton<InteractionService>()
            .AddSingleton<GlobalMusicService>()
            .AddSingleton<SearchService>()
            .AddSingleton<YoutubeService>()
            .AddSingleton<AudioServiceNative>()
            .AddSingleton<AudioService>()
            .AddTransient<GuildMusicService>()
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
        
        // Exception monitoring, logs all exceptions in the application (including handled ones)
        AppDomain.CurrentDomain.FirstChanceException += async (sender, eventArgs) =>
        {
            var ex = eventArgs.Exception;
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff");
            var msg = $"[{timestamp}] Exception occurred:\n{ex}\n\n";
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "error.log");
            await File.AppendAllTextAsync(fullPath, msg);
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
