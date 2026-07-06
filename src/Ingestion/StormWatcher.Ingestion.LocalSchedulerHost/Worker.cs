using StormWatcher.Ingestion.Scheduling;

namespace StormWatcher.Ingestion.LocalSchedulerHost;

public sealed class Worker(PollDispatchService pollDispatchService, ILogger<Worker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        do
        {
            try
            {
                await pollDispatchService.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PollDispatchService run failed; will retry next tick");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
