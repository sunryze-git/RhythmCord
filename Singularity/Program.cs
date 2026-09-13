using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Singularity.Configuration;
using Singularity.Features.Music;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Resolvers;
using Singularity.Infrastructure;

using NetCord;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Services.ApplicationCommands;
using Singularity.Features.General;
using Singularity.Features.Booru;

namespace Singularity;

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
        builder.Logging.AddFilter("Singularity", LogLevel.Debug);

        // DI registration
        builder.Services.AddHttpClient();
        builder.Services.AddDiscordGateway();
        builder.Services.AddApplicationCommands<ApplicationCommandInteraction, ApplicationCommandContext>();

        // Bot Support Services
        builder.Services.AddSingleton<ApplicationCommandService<ApplicationCommandContext>>();

        // Add Music Feature
        InfrastructureBootstrapper.AddMusicFeature(builder.Services);

        // Add Booru Feature
        InfrastructureBootstrapper.AddBooruFeature(builder.Services);

        // Resolvers Enumerable registration
        var conf = new ResolverSettings();
        builder.Configuration.GetSection("ResolverSettings").Bind(conf);
        builder.Services.AddScoped<IMediaResolver, YoutubeResolver>();

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
        host.AddApplicationCommandModule<BooruCommands>();

        await host.RunAsync();
    }
}
