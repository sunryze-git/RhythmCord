using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MusicBot.Features.Utility;

public class ConsoleInputService(ILogger<ConsoleInputService> logger, ResourceMonitorService resourceMonitor)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Console input handler started. Press 'r' for resources, 't' to toggle monitoring.");

        await Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
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
                                logger.LogInformation("Auto-monitoring: {Off}",
                                    resourceMonitor.IsMonitoringEnabled ? "ON" : "OFF");
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
        }, stoppingToken);
    }

    private void PrintHelp()
    {
        const string helpText = """
                                MusicBot Console Commands:
                                  'r' - Show current resource usage
                                  't' - Toggle automatic monitoring
                                  'h' - Show this help
                                  Ctrl+C - Shutdown bot
                                """;
        logger.LogInformation(helpText);
    }
}
