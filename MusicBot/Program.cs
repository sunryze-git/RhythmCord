using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicBot.Configuration;
using MusicBot.Features;
using MusicBot.Features.Audio;
using MusicBot.Features.Commands;
using MusicBot.Features.Media;
using MusicBot.Features.Media.Backends;
using MusicBot.Features.Media.Resolvers;
using MusicBot.Features.Queue;
using MusicBot.Infrastructure;

using NetCord;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Services.ApplicationCommands;

namespace MusicBot;

public abstract class Program
{
    public static async Task Main(string[] args)
    {
        InfrastructureBootstrapper.Initialize();
        var builder = Host.CreateApplicationBuilder(args);

        // Configure Logging
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fffffff] ";
            options.SingleLine = true;
            options.IncludeScopes = false;
        });
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddFilter("NetCord", LogLevel.Information);
        builder.Logging.AddFilter("MusicBot", LogLevel.Debug);

        // DI registration
        builder.Services.AddHttpClient();
        builder.Services.AddDiscordGateway();
        builder.Services.AddApplicationCommands<ApplicationCommandInteraction, ApplicationCommandContext>();

        // Bot Support Services
        builder.Services.AddSingleton<ApplicationCommandService<ApplicationCommandContext>>();
        builder.Services.AddSingleton<YoutubeBackend>();
        builder.Services.AddSingleton<GuildAudioInstanceOrchestrator>();
        builder.Services.AddSingleton<DlpBackend>();
        builder.Services.AddScoped<AudioService>();
        builder.Services.AddScoped<GuildAudioInstance>();
        builder.Services.AddScoped<QueueManager>();
        builder.Services.AddScoped<MediaResolver>();
        builder.Services.AddScoped<PlaybackHandler>();
        builder.Services.AddSingleton<GuildAudioInstanceOrchestrator>();

        // Resolvers Enumerable registration
        var conf = new ResolverSettings();
        builder.Configuration.GetSection("ResolverSettings").Bind(conf);
        builder.Services.AddScoped<IMediaResolver, DirectFileResolver>();
        builder.Services.AddScoped<IMediaResolver, SoundcloudResolver>();
        builder.Services.AddScoped<IMediaResolver, YoutubeResolver>();
        builder.Services.AddScoped<IMediaResolver, YtdlpResolver>();

        // Begin
        var host = builder.Build();

        // Configure lifetime management
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var orchestrator = host.Services.GetRequiredService<GuildAudioInstanceOrchestrator>();
        lifetime.ApplicationStopping.Register(() =>
        {
            orchestrator.CloseAllManagersAsync().AsTask().GetAwaiter().GetResult();
        });

        // Register Commands
        // Add modules from the current assembly
        host.AddApplicationCommandModule<FunCommands>();
        host.AddApplicationCommandModule<MusicCommands>();

        await host.RunAsync();
    }
}
