using System.Runtime.InteropServices;

using NetCord.Gateway.Voice;

namespace MusicBot.Infrastructure;

public static class InfrastructureBootstrapper
{
    private const int _rtldNow = 2;

    [DllImport("libdl.so", EntryPoint = "dlopen")]
    private static extern IntPtr dlopen(string fileName, int flags);

    internal static void Initialize()
    {
        if (!CheckOpusLibrary())
            throw new DllNotFoundException(
                "Required native library 'libopus' was not found. Please install libopus (for Debian/Ubuntu: 'apt install libopus0').");
        RegisterOpusDllImportResolver();
    }

    // Check for libopus presence on Linux by attempting to load common sonames and probing common library locations.
    private static bool CheckOpusLibrary()
    {
        // Only enforce on Linux where sonames are consistent; on other OSes assume platform packaging handles codecs.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return true;

        var candidates = new[] { "libopus.so.0", "libopus.so", "opus" };

        // Try best-effort to load by name (relies on system loader paths)
        foreach (var name in candidates)
            try
            {
                if (!NativeLibrary.TryLoad(name, out var handle)) continue;
                try
                {
                    return true;
                }
                finally
                {
                    try
                    {
                        NativeLibrary.Free(handle);
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                }
            }
            catch (Exception)
            {
                // ignored
            }

        // Search common library directories
        var searchDirs = new[]
        {
            "/usr/lib", "/usr/lib64", "/usr/local/lib", "/lib", "/lib64", Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };
        foreach (var dir in searchDirs.Distinct())
        foreach (var name in candidates)
        {
            var path = Path.Combine(dir, name);
            try
            {
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                    try
                    {
                        return true;
                    }
                    finally
                    {
                        try
                        {
                            NativeLibrary.Free(handle);
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                    }
            }
            catch
            {
                // ignored
            }
        }

        // Fallback to dlopen if available (libdl)
        try
        {
            foreach (var dir in searchDirs.Distinct())
            foreach (var name in candidates)
            {
                var path = Path.Combine(dir, name);
                if (!File.Exists(path))
                    continue;

                try
                {
                    var ptr = dlopen(path, _rtldNow);
                    if (ptr != IntPtr.Zero) return true;
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }
        catch
        {
            // ignore any dlopen/platform interop issues
        }

        return false;
    }

    private static void RegisterOpusDllImportResolver()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

        var netcordAssembly = typeof(Opus).Assembly;
        var candidates = new[] { "libopus.so.0", "libopus.so", "opus" };
        var searchDirs = new[]
        {
            "/usr/lib", "/usr/lib64", "/usr/local/lib", "/lib", "/lib64", Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        NativeLibrary.SetDllImportResolver(netcordAssembly, (name, _, _) =>
        {
            if (!string.Equals(name, "opus", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("libopus", StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;

            // Try common sonames (relies on system loader)
            foreach (var cand in candidates)
                try
                {
                    if (NativeLibrary.TryLoad(cand, out var handle) && handle != IntPtr.Zero) return handle;
                }
                catch (Exception)
                {
                    // ignored
                }

            // Probe common directories for exact files (including versioned sonames)
            foreach (var dir in searchDirs.Distinct())
                try
                {
                    if (!Directory.Exists(dir))
                        continue;

                    // exact candidate filenames
                    foreach (var cand in candidates)
                    {
                        var pathFull = Path.Combine(dir, cand);
                        if (File.Exists(pathFull))
                            try
                            {
                                if (NativeLibrary.TryLoad(pathFull, out var handle) && handle != IntPtr.Zero)
                                    return handle;
                            }
                            catch (Exception)
                            {
                                // ignored
                            }
                    }

                    // versioned files like libopus.so.0.8.0
                    foreach (var file in Directory.EnumerateFiles(dir, "libopus.so*").OrderByDescending(f => f))
                        try
                        {
                            if (NativeLibrary.TryLoad(file, out var handle) && handle != IntPtr.Zero) return handle;
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                }
                catch (Exception)
                {
                    // ignored
                }

            return IntPtr.Zero;
        });
    }
}
