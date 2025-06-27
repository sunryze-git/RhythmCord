using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicBot.Services.Media.Resolvers;
using MusicBot.Utilities;
using YoutubeExplode.Videos;

namespace MusicBot.Services.Utility;

public class ResourceMonitorService(ILogger<ResourceMonitorService> logger, IServiceProvider serviceProvider)
    : BackgroundService
{
    private readonly ILogger<ResourceMonitorService> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    internal bool IsMonitoringEnabled;

    private long _lastMemoryUsage;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
        {
            var proc = Process.GetCurrentProcess();
            var currentMemory = proc.WorkingSet64 / 1024 / 1024; // Convert to MB
            PrintMemoryWarning(currentMemory);
            if (IsMonitoringEnabled)
            {
                PrintResourceUsage();
            }
        }
    }
    
    public void ToggleMonitoring() => IsMonitoringEnabled = !IsMonitoringEnabled;

    internal void PrintResourceUsage()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var gcMemory = GC.GetTotalMemory(false) / 1024 / 1024;
            var workingSet = process.WorkingSet64 / 1024 / 1024;
            var privateMem = process.PrivateMemorySize64 / 1024 / 1024;
            var cpuTime = process.TotalProcessorTime;
            var threadCount = process.Threads.Count;

            var activeAudioSerivces = GetActiveAudioServicesCount();
            var resolverInfo = GetResolverInformation();
            var streamInfo = GetActiveStreamInformation();

            Console.WriteLine($"""
                               ═══════════════════════════════════════
                               RESOURCE USAGE - {DateTime.Now:HH:mm:ss}
                               ═══════════════════════════════════════
                               Memory Usage:
                                    GC Memory:      {gcMemory:N0} MB
                                    Working Set:    {workingSet:N0} MB
                                    Private Memory: {privateMem:N0} MB
                               Process Information:
                                    CPU Time:       {cpuTime:g}
                                    Thread Count:   {threadCount}
                               Audio Services:
                                    Active Streams: {activeAudioSerivces}
                                    Est. Audio Mem: ~{activeAudioSerivces * 0.2:F1} MB
                               GC Statistics:
                                    Gen 0: {GC.CollectionCount(0)}
                                    Gen 1: {GC.CollectionCount(1)}
                                    Gen 2: {GC.CollectionCount(2)}
                               Resolvers:
                               {resolverInfo}
                               Active Streams:
                               {streamInfo}
                               """);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
        }
        
    }
    
    private void PrintMemoryWarning(long currentMemory)
    {
        if (currentMemory > _lastMemoryUsage * 1.2)
        {
            _logger.LogWarning("Memory usage increased significantly: {CurrentMemory} MB", currentMemory);
        }
        _lastMemoryUsage = currentMemory;
    }

    private int GetActiveAudioServicesCount()
    {
        try
        {
            var globalMusicService = _serviceProvider.GetService<GlobalMusicService>();
            return globalMusicService?.NumberOfActiveManagers ?? 0;
        }
        catch
        {
            return 0;
        }
    }
    
    private string GetResolverInformation()
    {
        try
        {
            var resolvers = _serviceProvider.GetServices<IMediaResolver>();
            var resolverList = resolvers
                .OrderBy(r => r.Priority)
                .Select(r => $"                     [{r.Priority}] {r.Name}")
                .ToList();

            return resolverList.Count > 0 
                ? string.Join("\n", resolverList)
                : "                     No resolvers registered";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get resolver information");
            return "                     Error retrieving resolvers";
        }
    }
    
    private string GetActiveStreamInformation()
    {
        try
        {
            var globalMusicService = _serviceProvider.GetService<GlobalMusicService>();
            if (globalMusicService == null)
                return "                     No global music service available";

            var streamsByResolver = new Dictionary<string, int>();
            var totalStreams = 0;

            // Get all active guild music services
            foreach (var guildManager in globalMusicService.GetActiveManagers())
            {
                var currentSong = guildManager.PlaybackHandler.CurrentSong;
                if (currentSong == null) continue;
                totalStreams++;
                var resolverUsed = GetResolverUsedForSong(currentSong);
                    
                if (streamsByResolver.TryGetValue(resolverUsed, out var value))
                    streamsByResolver[resolverUsed] = ++value;
                else
                    streamsByResolver[resolverUsed] = 1;
            }

            if (totalStreams == 0)
                return "                     No active streams";

            var streamInfo = streamsByResolver
                .OrderByDescending(kvp => kvp.Value)
                .Select(kvp => $"                     {kvp.Key}: {kvp.Value} stream{(kvp.Value == 1 ? "" : "s")}")
                .ToList();

            streamInfo.Insert(0, $"                     Total: {totalStreams} active stream{(totalStreams == 1 ? "" : "s")}");
            
            return string.Join("\n", streamInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get stream information");
            return "                     Error retrieving stream info";
        }
    }
    
    private string GetResolverUsedForSong(IVideo song)
    {
        // Determine resolver based on song type/URL
        if (song is CustomSong customSong)
        {
            var url = customSong.Url;
            
            // Check if it's a direct audio file
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return "Cobalt"; // CustomSong usually comes from Cobalt
            var uri = new Uri(url);
            var ext = Path.GetExtension(uri.LocalPath).ToLowerInvariant();
            var audioExts = new[] { ".mp3", ".wav", ".flac", ".ogg", ".opus", ".m4a", ".aac" };
                
            if (audioExts.Contains(ext))
                return "DirectFile";

            return "Cobalt"; // CustomSong usually comes from Cobalt
        }
        
        // Check if it's from YouTube
        if (song.Url.Contains("youtube.com") || song.Url.Contains("youtu.be"))
            return "YouTube";
            
        return "Unknown";
    }
}