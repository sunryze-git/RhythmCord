using System.Runtime.ExceptionServices;
using CobaltApi;
using FFmpeg.AutoGen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MusicBot.Records;
using MusicBot.Services;
using MusicBot.Services.Audio;
using MusicBot.Services.Interactions;
using MusicBot.Services.Media.Backends;
using MusicBot.Services.Media.Resolvers;
using MusicBot.Services.Utility;
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
            builder.ClearProviders();
            
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
        
        var config = JsonConvert.DeserializeObject<Config>(await File.ReadAllTextAsync(ConfigPath));
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

        var commonHttpClient = new HttpClient();
        
        // Initialize the Service Collection, and load the client and application command service
        Services = new ServiceCollection()
            .AddSingleton(loggerFactory)
            .AddSingleton(client)
            .AddSingleton(commonHttpClient)
            .AddLogging()
            .AddSingleton<ApplicationCommandService<ApplicationCommandContext>>()
            .AddSingleton<InteractionService>()
            .AddSingleton<GlobalMusicService>()
            .AddSingleton<DlpBackend>()
            .AddSingleton<YoutubeBackend>()
            .AddSingleton<ResourceMonitorService>()
            .AddSingleton<ConsoleInputService>()
            .AddTransient<AudioServiceNative>()
            .AddSingleton(_ => new CobaltClient("http://192.168.1.91:9000"))
            .AddScoped<IMediaResolver, DirectFileResolver>()
            .AddScoped<IMediaResolver, YoutubeResolver>()
            .AddScoped<IMediaResolver, CobaltResolver>()
            .AddScoped<IMediaResolver, YtdlpResolver>()
            .AddScoped<IMediaResolver, SoundcloudResolver>()
            .AddScoped<IMediaResolver, SpotifyResolver>()
            .AddTransient<GuildMusicService>()
            .BuildServiceProvider();
        
        var resourceMonitor = Services.GetRequiredService<ResourceMonitorService>();
        var consoleInputService = Services.GetRequiredService<ConsoleInputService>();
        
        _ = resourceMonitor.StartAsync(CancellationToken.None);
        _ = consoleInputService.StartAsync(CancellationToken.None);
        
        // Initialize the application command service
        var applicationCommandService = Services.GetRequiredService<ApplicationCommandService<ApplicationCommandContext>>();
        applicationCommandService.AddModules(typeof(MusicBot).Assembly);
        
        // Initialize the interaction service
        var interactionService = Services.GetRequiredService<InteractionService>();
        
        client.Log += DiscordClient_LogAsync;
        client.InteractionCreate += interactionService.OnClientOnInteractionCreateAsync;

        // Log out when interrupted via keyboard
        Console.CancelKeyPress += delegate {
            _logger.LogInformation("Logging out!");
            client.CloseAsync().Wait();
        };
        
        // Exception monitoring, logs all exceptions in the application (including handled ones)
        AppDomain.CurrentDomain.FirstChanceException += OnException;
        
        await applicationCommandService.CreateCommandsAsync(client.Rest, client.Id, true);
        await client.StartAsync();
        await Task.Delay(Timeout.Infinite);
    }
    
    private static ValueTask DiscordClient_LogAsync(LogMessage logMessage)
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
    
    private static void OnException(object? sender, FirstChanceExceptionEventArgs ex)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.ffff");
            var msg = $"[{timestamp}] Exception occurred:\n{ex.Exception}\n\n";
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "error.log");
            File.AppendAllText(fullPath, msg);
        }
        catch
        {
            // If we fail to log the exception, we just ignore it.
        }
    }
}
