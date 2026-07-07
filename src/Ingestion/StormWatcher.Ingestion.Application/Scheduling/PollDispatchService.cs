using Microsoft.Extensions.Logging;

namespace StormWatcher.Ingestion.Scheduling;

/// <summary>
/// Checks active (Location, Provider) pairs against configured poll cadence and
/// enqueues PollLocationRequested for anything due. Idempotent / catch-up tolerant:
/// calling early is a no-op, calling late just delays polling slightly (§3.1.3).
/// </summary>
public sealed class PollDispatchService(ILogger<PollDispatchService> logger)
{
    public Task RunOnceAsync(CancellationToken cancellationToken)
    {
        // TODO: query active (Location, Provider) pairs + last-poll timestamps,
        // publish PollLocationRequested via MassTransit for anything due.
        logger.LogInformation("PollDispatchService.RunOnceAsync tick at {UtcNow} (stub — no due-check logic yet)", DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
}
