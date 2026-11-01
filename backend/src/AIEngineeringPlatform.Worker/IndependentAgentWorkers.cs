using AIEngineeringPlatform.Application.RuntimeV2;
using AIEngineeringPlatform.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIEngineeringPlatform.Worker;

// Bounded consumers share durable role-assigned tasks; tools remain in the orchestrator.
public sealed class IndependentAgentWorkers(IndependentAgentTaskProcessor processor, AgentRuntimeOptions options, ILogger<IndependentAgentWorkers> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.IsValid) throw new InvalidOperationException("Invalid independent worker capacity.");
        return Task.WhenAll(Enumerable.Range(0, options.MaxConcurrentTasks).Select(slot => WorkAsync(slot, stoppingToken)));
    }
    private async Task WorkAsync(int slot, CancellationToken ct)
    {
        var worker = "agent-worker/" + slot + "/" + Guid.NewGuid().ToString("N");
        while (!ct.IsCancellationRequested)
        {
            try { if (!await processor.ProcessNextAsync(worker, ct)) await Task.Delay(250, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning("Independent worker slot {Slot} unavailable: {ErrorType}", slot, error.GetType().Name);
                await Task.Delay(1000, ct);
            }
        }
    }
}
