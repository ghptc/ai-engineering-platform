using AIEngineeringPlatform.Application.RuntimeV2;
using AIEngineeringPlatform.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIEngineeringPlatform.Worker;

// Exactly two bounded role loops; never creates concurrent repository mutations.
public sealed class IndependentAgentWorkers(IndependentAgentTaskProcessor processor, ILogger<IndependentAgentWorkers> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        WorkAsync(AgentRole.BackendEngineer, stoppingToken), WorkAsync(AgentRole.TestEngineer, stoppingToken));
    private async Task WorkAsync(AgentRole role, CancellationToken ct)
    {
        var worker = role + "/" + Guid.NewGuid().ToString("N");
        while (!ct.IsCancellationRequested)
        {
            try { if (!await processor.ProcessOneAsync(role, worker, ct)) await Task.Delay(250, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning("Independent {Role} worker unavailable: {ErrorType}", role, error.GetType().Name);
                await Task.Delay(1000, ct);
            }
        }
    }
}
