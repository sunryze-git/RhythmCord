using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Singularity.Features.Music;
using Singularity.Features.Music.Backends;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Resolvers;
using Singularity.Features.Music.Player;
using Singularity.Features.Music.Services;
using NetCord.Gateway.Voice;
using Singularity.Features.Booru.Clients;
using Singularity.Features.Booru.Services;

namespace Singularity.Infrastructure;

public static class InfrastructureBootstrapper
{
    /// <summary>
    ///     Starts the external-tool checks in the background (yt-dlp alone takes ~0.4s to report its version) so they
    ///     overlap with host construction. Await the returned task before the bot starts handling work.
    /// </summary>
    internal static Task StartToolChecks() => Task.WhenAll(
        Task.Run(() => RequireExecutable("ffmpeg", "-version")),
        Task.Run(() => RequireExecutable("yt-dlp", "--version")));

    /// <summary>Fast, synchronous native-library setup that must happen before any voice code runs.</summary>
    internal static void Initialize()
    {
        RegisterOpusDllImportResolver();

        // Fail-fast check to ensure the environment is ready
        if (!CheckOpusLibrary())
        {
            throw new StartupException(
                "Required native library 'libopus' was not found. Please install libopus (e.g., 'apt install libopus0').");
        }
    }

    public static IServiceCollection AddMusicFeature(this IServiceCollection services)
    {
        // Shared, stateless services
        services.AddSingleton<GuildAudioInstanceOrchestrator>();
        services.AddSingleton<MediaResolver>();
        services.AddSingleton<ITrackSource>(sp => sp.GetRequiredService<MediaResolver>());
        services.AddSingleton(sp => new MusicSearch(
            sp.GetRequiredService<ITrackSource>(), sp.GetRequiredService<MusicOptions>(), sp.GetRequiredService<TimeProvider>()));

        // Backends and resolvers; MediaResolver tries them in the order set by Music:ResolverOrder.
        services.AddSingleton<YoutubeBackend>();
        services.AddSingleton<YoutubeResolver>();
        services.AddSingleton<YtdlpResolver>();
        services.AddSingleton<IMediaResolver>(sp => sp.GetRequiredService<YoutubeResolver>());
        services.AddSingleton<IMediaResolver>(sp => sp.GetRequiredService<YtdlpResolver>());

        // Per-guild state: one of each per guild, because the orchestrator resolves them inside a scope per guild.
        services.AddScoped<AudioService>();
        services.AddScoped<DiscordVoiceSession>();
        services.AddScoped<IVoiceSession>(sp => sp.GetRequiredService<DiscordVoiceSession>());
        services.AddScoped<ITrackPlayer>(sp => sp.GetRequiredService<DiscordVoiceSession>());
        services.AddScoped<ChannelNotifier>();
        services.AddScoped<IPlayerNotifier>(sp => sp.GetRequiredService<ChannelNotifier>());
        services.AddScoped<GuildPlayer>();
        services.AddScoped<GuildAudioInstance>();

        return services;
    }

    public static IServiceCollection AddBooruFeature(this IServiceCollection services)
    {
        // E621Client sets its own base address and User-Agent.
        services.AddHttpClient<IE621Client, E621Client>();
        services.AddSingleton<BooruService>();

        return services;
    }

    private static void RequireExecutable(string name, string versionArgument)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = name,
                ArgumentList = { versionArgument },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new InvalidOperationException("The process could not be started.");

            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                process.Kill();
                throw new TimeoutException($"'{name} {versionArgument}' did not finish in time.");
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"'{name} {versionArgument}' exited with code {process.ExitCode}.");
        }
        catch (Exception ex)
        {
            throw new StartupException($"Required program '{name}' could not be run. Make sure it is installed and on PATH.", ex);
        }
    }

    private static bool CheckOpusLibrary()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return true;

        // Simply relying on TryLoad tests the OS's native resolution paths.
        if (NativeLibrary.TryLoad("libopus.so.0", typeof(InfrastructureBootstrapper).Assembly, DllImportSearchPath.SafeDirectories, out var handle))
        {
            NativeLibrary.Free(handle);
            return true;
        }

        return false;
    }

    private static void RegisterOpusDllImportResolver()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

        NativeLibrary.SetDllImportResolver(typeof(Opus).Assembly, (libraryName, assembly, searchPath) =>
        {
            // Only intercept calls looking for "opus"
            if (!string.Equals(libraryName, "opus", StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero;
            }

            string[] candidates = { "libopus.so.0", "libopus.so" };

            // Let the OS dynamic linker do the work
            foreach (var candidate in candidates)
            {
                if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero;
        });
    }
}
