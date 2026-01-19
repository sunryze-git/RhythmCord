using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MusicBot.Configuration;
using MusicBot.Features;
using MusicBot.Features.Audio;
using MusicBot.Features.Commands;
using MusicBot.Features.Media;
using MusicBot.Features.Media.Backends;
using MusicBot.Features.Media.Resolvers;
using MusicBot.Features.Queue;
using MusicBot.Features.Utility;
using MusicBot.Infrastructure;

using NetCord;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Services.ApplicationCommands;

namespace MusicBot;

public abstract class Program
{
    public delegate GuildAudioInstance GuildAudioInstanceFactory(ApplicationCommandContext context);

    public static async Task Main(string[] args)
    {
        InfrastructureBootstrapper.Initialize();
        var builder = Host.CreateApplicationBuilder(args);

        // DI registration
        builder.Services.AddHttpClient();
        builder.Services.AddLogging();
        builder.Services.AddDiscordGateway();
        builder.Services.AddApplicationCommands<ApplicationCommandInteraction, ApplicationCommandContext>();

        // Bot Support Services
        builder.Services.AddSingleton<ResourceMonitorService>();
        builder.Services.AddHostedService<ResourceMonitorService>(sp =>
            sp.GetRequiredService<ResourceMonitorService>());
        builder.Services.AddHostedService<ConsoleInputService>();
        builder.Services.AddSingleton<ApplicationCommandService<ApplicationCommandContext>>();
        builder.Services.AddSingleton<YoutubeBackend>();
        builder.Services.AddSingleton<GuildAudioInstanceOrchestrator>();
        builder.Services.AddSingleton<DlpBackend>();
        builder.Services.AddScoped<AudioServiceNative>();
        builder.Services.AddScoped<GuildAudioInstance>();
        builder.Services.AddScoped<QueueManager>();
        builder.Services.AddScoped<MediaResolver>();
        builder.Services.AddScoped<PlaybackHandler>();
        builder.Services.AddSingleton<GuildAudioInstanceFactory>(provider => context =>
        {
            var scope = provider.CreateScope();
            var instance = scope.ServiceProvider.GetRequiredService<GuildAudioInstance>();
            instance.Initialize(context);
            return instance;
        });

        // Resolvers Enumerable registration
        var conf = new ResolverSettings();
        builder.Configuration.GetSection("ResolverSettings").Bind(conf);
        if (conf.EnableCobalt) builder.Services.AddScoped<IMediaResolver, CobaltResolver>();
        if (conf.EnableDirect) builder.Services.AddScoped<IMediaResolver, DirectFileResolver>();
        if (conf.EnableSoundCloud) builder.Services.AddScoped<IMediaResolver, SoundcloudResolver>();
        if (conf.EnableYouTube) builder.Services.AddScoped<IMediaResolver, YoutubeResolver>();
        if (conf.EnableYtdlp) builder.Services.AddScoped<IMediaResolver, YtdlpResolver>();

        // Begin
        var host = builder.Build();

        // Configure lifetime management
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var orchestrator = host.Services.GetRequiredService<GuildAudioInstanceOrchestrator>();
        lifetime.ApplicationStopping.Register(() => { orchestrator.CloseAllManagers(); });

        // Register Commands
        // Add modules from the current assembly
        host.AddApplicationCommandModule<FunCommands>();
        host.AddApplicationCommandModule<MusicCommands>();

        await host.RunAsync();
    }
}
