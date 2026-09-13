using System.Diagnostics;
using System.Runtime.InteropServices;
using FFmpeg.Loader;
using Microsoft.Extensions.DependencyInjection;
using Singularity.Features.Music.Backends;
using Singularity.Features.Music.Models;
using Singularity.Features.Music.Resolvers;
using Singularity.Features.Music.Services;
using NetCord.Gateway.Voice;
using Singularity.Features.Booru.Clients;
using Singularity.Features.Booru.Services;

namespace Singularity.Infrastructure;

public static class InfrastructureBootstrapper
{
    internal static void Initialize()
    {
        LoadFfmpegLibraries();
        RegisterOpusDllImportResolver();

        // Fail-fast check to ensure the environment is ready
        if (!CheckOpusLibrary())
        {
            throw new DllNotFoundException(
                "Required native library 'libopus' was not found. Please in(89%)stall libopus (e.g., 'apt install libopus0').");
        }
    }

    public static IServiceCollection AddMusicFeature(this IServiceCollection services)
    {
        // Core Audio & Dispatch Services
        services.AddSingleton<AudioService>();
        services.AddSingleton<GuildAudioInstanceOrchestrator>();
        services.AddSingleton<MediaResolver>();

        // Backends & Concrete Resolvers
        services.AddSingleton<YoutubeBackend>();
        services.AddSingleton<YoutubeResolver>();
        services.AddSingleton<YtdlpResolver>();

        // Transient Guild State Controllers
        services.AddTransient<QueueManager>();
        services.AddTransient<PlaybackHandler>();
        services.AddTransient<GuildAudioInstance>();

        return services;
    }

    public static IServiceCollection AddBooruFeature(this IServiceCollection services)
    {
        services.AddHttpClient<IE621Client, E621Client>(client =>
        {
            client.BaseAddress = new Uri("https://e621.net/");
        });
        services.AddSingleton<BooruService>();

        return services;
    }

    private static void LoadFfmpegLibraries()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch
        {
            throw new InvalidOperationException("FFmpeg executable could not be found.");
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
