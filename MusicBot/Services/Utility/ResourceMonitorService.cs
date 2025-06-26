using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MusicBot.Services.Utility;

public class ResourceMonitorService(ILogger<ResourceMonitorService> logger, IServiceProvider serviceProvider)
    : BackgroundService
{
    private readonly ILogger<ResourceMonitorService> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    internal bool _isMonitoringEnabled;
    
    private long lastMemoryUsage = 0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
        {
            var proc = Process.GetCurrentProcess();
            var currentMemory = proc.WorkingSet64 / 1024 / 1024; // Convert to MB
            PrintMemoryWarning(currentMemory);
            if (_isMonitoringEnabled)
            {
                PrintResourceUsage();
            }
        }
    }
    
    public void ToggleMonitoring() => _isMonitoringEnabled = !_isMonitoringEnabled;

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
                               """);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
        }
        
    }
    
    private void PrintMemoryWarning(long currentMemory)
    {
        if (currentMemory > lastMemoryUsage * 1.2)
        {
            _logger.LogWarning("Memory usage increased significantly: {CurrentMemory} MB", currentMemory);
        }
        lastMemoryUsage = currentMemory;
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
}