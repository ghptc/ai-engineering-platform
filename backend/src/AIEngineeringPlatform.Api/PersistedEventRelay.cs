using AIEngineeringPlatform.Application;
namespace AIEngineeringPlatform.Api;

// One global cursor preserves cross-kind evidence ordering; workers never own browser connections.
public sealed class PersistedEventRelay(IPlatformStore store, IExecutionPublisher publisher, ILogger<PersistedEventRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long cursor = 0;
        var initialized = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                if (!initialized)
                {
                    var latest = await store.OperationalRecordsAsync(0, 1, true, deadline.Token);
                    cursor = latest.FirstOrDefault()?.Sequence ?? 0;
                    initialized = true;
                }
                var batch = await store.OperationalRecordsAsync(cursor, 100, false, deadline.Token);
                foreach (var entry in batch)
                {
                    await publisher.PublishAsync(entry, deadline.Token);
                    cursor = entry.Sequence;
                }
                if (batch.Count < 100) await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Persisted event relay unavailable; history remains authoritative. Retrying in two seconds."); await Task.Delay(2000, stoppingToken); }
        }
    }
}
