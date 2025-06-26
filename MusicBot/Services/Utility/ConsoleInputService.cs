using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MusicBot.Services.Utility;

public class ConsoleInputService(ILogger<ConsoleInputService> logger, ResourceMonitorService resourceMonitor)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Console input handler started. Press 'r' for resources, 't' to toggle monitoring.");
        
        await Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        
                        switch (char.ToLower(key.KeyChar))
                        {
                            case 'r':
                                resourceMonitor.PrintResourceUsage();
                                break;
                            case 't':
                                resourceMonitor.ToggleMonitoring();
                                Console.WriteLine($"Auto-monitoring: {(resourceMonitor._isMonitoringEnabled ? "ON" : "OFF")}");
                                break;
                            case 'h':
                                PrintHelp();
                                break;
                        }
                    }
                    
                    await Task.Delay(100, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in console input handler");
                }
            }
        }, stoppingToken);
    }

    private static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("🎹 MusicBot Console Commands:");
        Console.WriteLine("  'r' - Show current resource usage");
        Console.WriteLine("  't' - Toggle automatic monitoring");
        Console.WriteLine("  'h' - Show this help");
        Console.WriteLine("  Ctrl+C - Shutdown bot");
        Console.WriteLine();
    }
}