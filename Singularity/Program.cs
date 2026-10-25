using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Singularity.Features.Music.Models;
using Singularity.Infrastructure;
using Singularity.Observability;

using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using Singularity.Features.General;
using Singularity.Features.Booru;
using Singularity.Features.Music;
using Singularity.Features.Music.Backends;

namespace Singularity;

public abstract class Program
{
    // Set once a fatal error has been written to the log, so the console does not repeat the stack trace.
    private static bool s_fatalLogged;

    /// <summary>Exit codes: 0 clean, 78 configuration/environment problem (sysexits EX_CONFIG), 1 anything else.</summary>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            await RunAsync(args);
            return 0;
        }
        catch (StartupException ex)
        {
            // A known setup problem: one clear line, no stack trace.
            await Console.Error.WriteLineAsync($"error: {ex.Message}");
            if (ex.InnerException is { } cause) await Console.Error.WriteLineAsync($"  cause: {cause.Message}");
            return 78;
        }
        catch (OperationCanceledException)
        {
            return 0; // shutting down
        }
        catch (Exception ex)
        {
            // If logging was up the full detail is already in the log; otherwise this is the only record.
            await Console.Error.WriteLineAsync(s_fatalLogged ? $"fatal: {ex.GetType().Name}: {ex.Message} (details are in the log above)" : $"fatal: {ex}");
            return 1;
        }
    }

    private static async Task RunAsync(string[] args)
    {
        var startup = Stopwatch.StartNew();

        // External tool checks run in the background while the host is being built.
        var toolChecks = InfrastructureBootstrapper.StartToolChecks();
        InfrastructureBootstrapper.Initialize();
        var builder = Host.CreateApplicationBuilder(args);

        // Token sources, lowest to highest precedence: appsettings.json, .NET user-secrets (per-user secret store,
        // see README), then environment variables (Discord__Token) -- the latter re-added so it still wins.
        // User-secrets is optional: servers/systemd simply use the environment variable.
        builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
        builder.Configuration.AddEnvironmentVariables();

        // Fail early with a clear message instead of a gateway error later.
        var token = builder.Configuration["Discord:Token"];
        if (string.IsNullOrWhiteSpace(token) || token.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
            throw new StartupException(
                "No Discord token configured. Set the Discord__Token environment variable, " +
                "or for local runs store it with:\n" +
                "  dotnet user-secrets set \"Discord:Token\" \"<token>\" --project Singularity/Singularity.csproj");

        var metrics = new BotMetrics();
        var logBuffer = new LogBuffer();
        ConfigureLogging(builder, metrics, logBuffer);

        // DI registration
        builder.Services.AddHttpClient();
        builder.Services.AddDiscordGateway();
        builder.Services.AddApplicationCommands<ApplicationCommandInteraction, ApplicationCommandContext>(
            options => options.ResultHandler = new CommandErrorHandler());
        builder.Services.AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>(
            options => options.ResultHandler = new ComponentErrorHandler<ButtonInteractionContext>());
        builder.Services.AddComponentInteractions<StringMenuInteraction, StringMenuInteractionContext>(
            options => options.ResultHandler = new ComponentErrorHandler<StringMenuInteractionContext>());
        builder.Services.AddSingleton<CommandCooldown>();
        builder.Services.AddSingleton(TimeProvider.System);

        // Tunables (Music:* and Booru:* settings; defaults are in the options classes)
        builder.Services.AddSingleton(builder.Configuration.GetSection("Music").Get<MusicOptions>() ?? new MusicOptions());
        builder.Services.AddSingleton(builder.Configuration.GetSection("Booru").Get<BooruOptions>() ?? new BooruOptions());

        // Features
        builder.Services.AddMusicFeature();
        builder.Services.AddBooruFeature();

        // Observability: aggregate-only stats, one place for reporting errors, a read-only local dashboard
        builder.Services.AddSingleton(metrics);
        builder.Services.AddSingleton(logBuffer);
        builder.Services.AddSingleton<ErrorReporter>();
        builder.Services.AddSingleton<StatsService>();
        builder.Services.AddHostedService<DashboardServer>();

        // Begin
        var host = builder.Build();
        await toolChecks;

        metrics.StartupMs = startup.ElapsedMilliseconds;
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var errors = host.Services.GetRequiredService<ErrorReporter>();
        logger.LogDebug("Startup checks and host build finished in {ElapsedMs} ms.", metrics.StartupMs);

        InstallCrashLogging(logger, errors);

        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() =>
        {
            logger.LogInformation("Started in {ElapsedMs} ms.", startup.ElapsedMilliseconds);
            _ = host.Services.GetRequiredService<YoutubeBackend>().WarmUpAsync();
        });

        GatewayClient? client;
        try
        {
            client = host.Services.GetService<GatewayClient>();
        }
        catch (ArgumentException ex)
        {
            // NetCord rejects a token that is not even shaped like one.
            throw new StartupException("The Discord token is not valid. Check Discord__Token / the stored secret.", ex);
        }

        if (client is not null)
        {
            metrics.ObserveGuilds(() => client.Cache.Guilds.Count);
            client.Ready += args =>
            {
                metrics.GatewayConnected = true;
                logger.LogInformation("Connected to Discord as {User}.", args.User.Username);
                return ValueTask.CompletedTask;
            };
        }

        // Close guild audio sessions (leave voice, stop ffmpeg/yt-dlp) while the gateway is still connected
        var orchestrator = host.Services.GetRequiredService<GuildAudioInstanceOrchestrator>();
        lifetime.ApplicationStopping.Register(() =>
        {
            metrics.GatewayConnected = false;
            try
            {
                orchestrator.CloseAllManagersAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                errors.Record(ex, "shutdown.sessions", "Closing the voice sessions failed during shutdown.");
            }
        });

        // Register Commands
        host.AddApplicationCommandModule<FunCommands>();
        host.AddApplicationCommandModule<MusicCommands>();
        host.AddApplicationCommandModule<BooruCommands>();
        host.AddComponentInteractionModule<MusicButtons>();
        host.AddComponentInteractionModule<MusicMenus>();

        try
        {
            await host.RunAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The host stops when a hosted service (e.g. the Discord gateway) fails. Make sure the reason is in the log.
            logger.LogCritical(ex, "The bot stopped because of an unhandled error.");
            s_fatalLogged = true;
            metrics.Error("unhandled.host");
            throw;
        }
    }

    // Faults that nothing awaits would otherwise vanish, or kill the process without a log line.
    private static void InstallCrashLogging(ILogger logger, ErrorReporter errors)
    {
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            errors.Record(e.Exception.Flatten(), "unhandled.task", "A background task failed and nobody was waiting for it.");
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                logger.LogCritical(ex, "Unhandled exception (process terminating: {Terminating})", e.IsTerminating);
            else
                logger.LogCritical("Unhandled non-exception error: {Error}", e.ExceptionObject);
        };
    }

    private static void ConfigureLogging(HostApplicationBuilder builder, BotMetrics metrics, LogBuffer logBuffer)
    {
        // Quiet defaults, at the *lowest* configuration precedence so appsettings.json or environment variables
        // (e.g. Logging__LogLevel__Singularity=Debug, Logging__LogLevel__NetCord=Information) can override them.
        builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource
        {
            InitialData = new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Information",
                ["Logging:LogLevel:Microsoft"] = "Warning",
                ["Logging:LogLevel:System.Net.Http"] = "Warning",
                ["Logging:LogLevel:NetCord"] = "Warning",
                ["Logging:LogLevel:Singularity"] = "Information"
            }
        });

        builder.Logging.ClearProviders();

        // LOG_FORMAT=json for log shippers; otherwise a compact, coloured single-line format.
        if (string.Equals(Environment.GetEnvironmentVariable("LOG_FORMAT"), "json", StringComparison.OrdinalIgnoreCase))
        {
            builder.Logging.AddJsonConsole(o =>
            {
                o.IncludeScopes = true;
                o.UseUtcTimestamp = true;
                o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
            });
        }
        else
        {
            // IncludeScopes: guild/user context is shown after the message (console only, never on the dashboard).
            builder.Services.AddSingleton<ConsoleFormatter, CompactConsoleFormatter>();
            builder.Services.Configure<ConsoleFormatterOptions>(o => o.IncludeScopes = true);
            builder.Logging.AddConsole(o => o.FormatterName = CompactConsoleFormatter.FormatterName);
        }

        // Recent events + warning/error counters for the dashboard (redacted, no scopes).
        builder.Logging.AddProvider(new BufferLoggerProvider(logBuffer, metrics));
    }
}
