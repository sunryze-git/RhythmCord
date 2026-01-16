using System.Runtime.ExceptionServices;
using CobaltApi;
using FFmpeg.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MusicBot.Records;
using MusicBot.Services;
using MusicBot.Services.Audio;
using MusicBot.Services.Interactions;
using MusicBot.Services.Media.Backends;
using MusicBot.Services.Media.Resolvers;
using MusicBot.Services.Utility;
using MusicBot.Utilities;
using NetCord;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;
using Newtonsoft.Json;
using System.Runtime.InteropServices;

namespace MusicBot;

public static class MusicBot
{
    private const string ConfigPath = "config.json";
    private static ILogger? _logger;
    internal static IServiceProvider? Services;
    internal static readonly IServiceCollection ServiceCollection = new ServiceCollection();
    public delegate GuildAudioInstance GuildAudioInstanceFactory(ApplicationCommandContext context);
    private const int RtldNow = 2;
    
    public static async Task Main()
    {
        // Load FFMPEG libraries first before logger initialization.
        LoadFfmpegLibraries();
        
        // Initialize Logger
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            
            builder
                .AddFilter("Microsoft", LogLevel.Warning)
                .AddFilter("System", LogLevel.Warning)
                .AddFilter("Discord", LogLevel.Information)
                .AddFilter("MusicBot", LogLevel.Debug)
                .AddProvider(new FullLineColorConsoleLoggerProvider());
        });
        
        _logger = loggerFactory.CreateLogger("MusicBot");
        _logger.LogInformation("Logger configured.");

        // Ensure libopus is available on Linux before proceeding
        if (!CheckOpusLibrary(_logger))
        {
            _logger.LogCritical("Required native library 'libopus' was not found. Please install libopus (for Debian/Ubuntu: 'apt install libopus0') and restart the application.");
            await Task.Delay(1000);
            return;
        }

        RegisterOpusDllImportResolver(_logger);
        
        // Load Bot Configuration
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

        var commonHttpClient = new HttpClient();
        
        // Initialize the Service Collection, and load the client and application command service
        ServiceCollection.AddSingleton(commonHttpClient);
        ServiceCollection.AddSingleton(loggerFactory);
        ServiceCollection.AddSingleton(client);
        ServiceCollection.AddSingleton(commonHttpClient);
        ServiceCollection.AddLogging();
        ServiceCollection.AddSingleton<ApplicationCommandService<ApplicationCommandContext>>();
        ServiceCollection.AddSingleton<InteractionService>();
        ServiceCollection.AddSingleton<GuildAudioInstanceOrchestrator>();
        ServiceCollection.AddSingleton<DlpBackend>();
        ServiceCollection.AddSingleton<YoutubeBackend>();
        ServiceCollection.AddSingleton<ResourceMonitorService>();
        ServiceCollection.AddSingleton<ConsoleInputService>();
        ServiceCollection.AddScoped<AudioServiceNative>();
        ServiceCollection.AddSingleton(_ => new CobaltClient("http://192.168.1.91:9000"));
        ServiceCollection.AddScoped<IMediaResolver, DirectFileResolver>();
        ServiceCollection.AddScoped<IMediaResolver, YoutubeResolver>();
        ServiceCollection.AddScoped<IMediaResolver, CobaltResolver>();
        ServiceCollection.AddScoped<IMediaResolver, YtdlpResolver>();
        ServiceCollection.AddScoped<IMediaResolver, SoundcloudResolver>();
        ServiceCollection.AddScoped<GuildAudioInstance>();
        ServiceCollection.AddScoped<GuildAudioInstanceFactory>(provider => context =>
        {
            var instance = provider.GetRequiredService<GuildAudioInstance>();
            instance.Initialize(context);
            return instance;
        });
        
        Services = ServiceCollection.BuildServiceProvider();
        
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
            _ => LogLevel.None
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

    private static void LoadFfmpegLibraries()
    {
        var search = FFmpegLoader.SearchPaths("/usr/lib64")
            .ThenSearchSystem()
            .ThenSearchApplication()
            .ThenSearchEnvironmentPaths("LD_LIBRARY_PATH");
        Console.WriteLine(search.Load("avcodec"));
        Console.WriteLine(search.Load("avformat"));
        Console.WriteLine(search.Load("swresample"));
    }
    
    // Check for libopus presence on Linux by attempting to load common sonames and probing common library locations.
    private static bool CheckOpusLibrary(ILogger? logger)
    {
        // Only enforce on Linux where sonames are consistent; on other OSes assume platform packaging handles codecs.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return true;

        var candidates = new[] { "libopus.so.0", "libopus.so", "opus" };

        // Try best-effort to load by name (relies on system loader paths)
        foreach (var name in candidates)
        {
            try
            {
                if (NativeLibrary.TryLoad(name, out var handle))
                {
                    try
                    {
                        logger?.LogDebug("Found libopus via NativeLibrary: {Name}", name);
                        return true;
                    }
                    finally
                    {
                        try { NativeLibrary.Free(handle); } catch (Exception ex) { logger?.LogDebug(ex, "NativeLibrary.Free failed for {Name}", name); }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "NativeLibrary.TryLoad threw for {Name}", name);
            }
        }

        // Search common library directories
        var searchDirs = new[] { "/usr/lib", "/usr/lib64", "/usr/local/lib", "/lib", "/lib64", Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var dir in searchDirs.Distinct())
        {
            foreach (var name in candidates)
            {
                var path = Path.Combine(dir, name);
                try
                {
                    if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                    {
                        try
                        {
                            logger?.LogDebug("Found libopus at path: {Path}", path);
                            return true;
                        }
                        finally
                        {
                            try { NativeLibrary.Free(handle); } catch (Exception ex) { logger?.LogDebug(ex, "NativeLibrary.Free failed for path {Path}", path); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "Failed loading libopus path {Path}", path);
                }
            }
        }

        // Fallback to dlopen if available (libdl)
        try
        {
            foreach (var dir in searchDirs.Distinct())
            {
                foreach (var name in candidates)
                {
                    var path = Path.Combine(dir, name);
                    if (!File.Exists(path))
                        continue;

                    try
                    {
                        var ptr = dlopen(path, RtldNow);
                        if (ptr != IntPtr.Zero)
                        {
                            logger?.LogDebug("Found libopus via dlopen at {Path}", path);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogDebug(ex, "dlopen failed for {Path}", path);
                    }
                }
            }
        }
        catch
        {
            // ignore any dlopen/platform interop issues
        }

        return false;
    }
   
    private static bool RegisterOpusDllImportResolver(ILogger? logger)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return true;

        var netcordAssembly = typeof(NetCord.Gateway.Voice.Opus).Assembly;
        var candidates = new[] { "libopus.so.0", "libopus.so", "opus" };
        var searchDirs = new[] { "/usr/lib", "/usr/lib64", "/usr/local/lib", "/lib", "/lib64", Directory.GetCurrentDirectory(), AppContext.BaseDirectory };

        NativeLibrary.SetDllImportResolver(netcordAssembly, (name, assembly, path) =>
        {
            if (!string.Equals(name, "opus", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("libopus", StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;

            // Try common sonames (relies on system loader)
            foreach (var cand in candidates)
            {
                try
                {
                    if (NativeLibrary.TryLoad(cand, out var handle) && handle != IntPtr.Zero)
                    {
                        logger?.LogDebug("DllImport resolver loaded {Name} via NativeLibrary.TryLoad.", cand);
                        return handle;
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "NativeLibrary.TryLoad threw for {Name}", cand);
                }
            }

            // Probe common directories for exact files (including versioned sonames)
            foreach (var dir in searchDirs.Distinct())
            {
                try
                {
                    if (!Directory.Exists(dir))
                        continue;

                    // exact candidate filenames
                    foreach (var cand in candidates)
                    {
                        var pathFull = Path.Combine(dir, cand);
                        if (File.Exists(pathFull))
                        {
                            try
                            {
                                if (NativeLibrary.TryLoad(pathFull, out var handle) && handle != IntPtr.Zero)
                                {
                                    logger?.LogDebug("DllImport resolver loaded {Path}", pathFull);
                                    return handle;
                                }
                            }
                            catch (Exception ex)
                            {
                                logger?.LogDebug(ex, "NativeLibrary.TryLoad threw for {Path}", pathFull);
                            }
                        }
                    }

                    // versioned files like libopus.so.0.8.0
                    foreach (var file in Directory.EnumerateFiles(dir, "libopus.so*").OrderByDescending(f => f))
                    {
                        try
                        {
                            if (NativeLibrary.TryLoad(file, out var handle) && handle != IntPtr.Zero)
                            {
                                logger?.LogDebug("DllImport resolver loaded discovered file {Path}", file);
                                return handle;
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.LogDebug(ex, "NativeLibrary.TryLoad threw for discovered file {Path}", file);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "Error enumerating {Dir}", dir);
                }
            }

            logger?.LogDebug("DllImport resolver could not resolve {Name}", name);
            return IntPtr.Zero;
        });

        logger?.LogDebug("Registered DllImport resolver for NetCord 'opus'.");
        return true;
    }

    [DllImport("libdl.so", EntryPoint = "dlopen")]
    private static extern IntPtr dlopen(string fileName, int flags);
}
