using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Agents;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Orchestration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
namespace AIEngineeringPlatform.Worker;

public sealed class EngineeringWorker(IPlatformStore store, EngineeringWorkflow workflow, GitHubWebhookProcessor webhooks, IExecutionPublisher publisher, IConfiguration configuration, ILogger<EngineeringWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialized = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!initialized)
                {
                    await store.InitializeAsync(stoppingToken);
                    if ((await store.AgentsAsync(stoppingToken)).Count == 0) foreach (var definition in AgentCatalog.Defaults(configuration["OPENAI_DEFAULT_MODEL"] ?? "")) await store.SaveAgentAsync(definition, stoppingToken);
                    foreach (var definition in await store.AgentsAsync(stoppingToken))
                    {
                        var addedTools = definition.Role is AgentRole.BackendEngineer or AgentRole.FrontendEngineer or AgentRole.TestEngineer
                            ? new[] { "RunCommand", "RunTests", "ReconcileRepository", "ReadPullRequest", "SelectBranch" }
                            : new[] { "ReconcileRepository", "ReadPullRequest" };
                        var tools = definition.AllowedTools.Union(addedTools).ToArray();
                        const string reconciliationInstructions = " Inspect actual repository files and ReconcileRepository PR facts before editing. ReadRepository advertises applicable TestProfiles. SelectBranch {branch,commit} selects an exact existing engineering PR head, never the default branch. Read its source and diff before deciding whether criteria already hold. Do not manufacture changes. A no-op is verified through current tests, QA and review; return ALREADY_SATISFIED when appropriate. Use node profile for dependency-free JavaScript, never dotnet or npm ci without required manifests/lockfiles. Business ambiguity returns AWAITING_HUMAN_INPUT; missing capabilities return BLOCKED.";
                        if (tools.Length == definition.AllowedTools.Length && definition.SystemInstructions.Contains(reconciliationInstructions, StringComparison.Ordinal)) continue;
                        await store.AppendAsync(new() { Kind = "instructionVersion", Name = definition.Id, Content = System.Text.Json.JsonSerializer.Serialize(definition), Metadata = new() { ["migration"] = "task-reconciliation-and-stack-profiles" } }, stoppingToken);
                        await store.SaveAgentAsync(definition with { AllowedTools = tools, SystemInstructions = definition.SystemInstructions + reconciliationInstructions, InstructionVersion = definition.InstructionVersion + 1 }, stoppingToken);
                    }
                    foreach (var definition in await store.AgentsAsync(stoppingToken))
                    {
                        if (definition.SystemInstructions.Contains(AgentCatalog.PlanningInstructions, StringComparison.Ordinal)) continue;
                        await store.AppendAsync(new() { Kind = "instructionVersion", Name = definition.Id, Content = System.Text.Json.JsonSerializer.Serialize(definition), Metadata = new() { ["migration"] = "stage-aware-planning" } }, stoppingToken);
                        await store.SaveAgentAsync(definition with { SystemInstructions = definition.SystemInstructions + AgentCatalog.PlanningInstructions, InstructionVersion = definition.InstructionVersion + 1 }, stoppingToken);
                    }
                    foreach (var definition in await store.AgentsAsync(stoppingToken))
                    {
                        if (definition.SystemInstructions.Contains(AgentCatalog.ImplementationInstructions, StringComparison.Ordinal)) continue;
                        await store.AppendAsync(new() { Kind = "instructionVersion", Name = definition.Id, Content = System.Text.Json.JsonSerializer.Serialize(definition), Metadata = new() { ["migration"] = "greenfield-implementation-handoff" } }, stoppingToken);
                        await store.SaveAgentAsync(definition with { SystemInstructions = definition.SystemInstructions + AgentCatalog.ImplementationInstructions, InstructionVersion = definition.InstructionVersion + 1 }, stoppingToken);
                    }
                    initialized = true;
                }
                foreach (var entry in await store.FlushOutboxAsync(stoppingToken)) await publisher.PublishAsync(entry, stoppingToken);
                await webhooks.ProcessPendingAsync(stoppingToken);
                await ReconcileApprovalsAsync(stoppingToken);
                var owner = Guid.NewGuid().ToString("N");
                var run = await store.ClaimAsync(owner, TimeSpan.FromMinutes(2), stoppingToken);
                if (run == null) { await Task.Delay(1000, stoppingToken); continue; }
                using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var heartbeat = HeartbeatAsync(run.Id, owner, execution);
                try { await workflow.ProcessAsync(run, owner, execution.Token); }
                catch (ConcurrencyException) { logger.LogInformation("Execution {RunId} ownership or control state changed", run.Id); }
                catch (OperationCanceledException) when (execution.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    var executorFailure = exception as ExecutorFailureException ?? (exception as ExecutorEvidenceException)?.Failure;
                    var persistenceFailure = exception as PersistenceFailureException ?? (exception as ExecutorEvidenceException)?.PersistenceFailure;
                    if (executorFailure != null)
                        logger.LogWarning("Executor failure for run {RunId}: operation {OperationId}, HTTP {HttpStatus}, category {Category}, reason {Reason}, outcomeUnknown {OutcomeUnknown}; evidencePersistenceFailed {EvidencePersistenceFailed}", run.Id, executorFailure.OperationId, executorFailure.StatusCode, executorFailure.Category, executorFailure.Reason, executorFailure.OutcomeUnknown, exception is ExecutorEvidenceException);
                    if (persistenceFailure != null)
                        logger.LogWarning("Persistence failure for run {RunId}: code {Code}, index {Index}, record {RecordId}, kind {Kind}, identityConflict {IdentityConflict}", run.Id, persistenceFailure.Code, persistenceFailure.Index, persistenceFailure.RecordId, persistenceFailure.RecordKind, persistenceFailure.IdentityConflict);
                    logger.LogWarning("Execution {RunId} stage {Stage} failed with {ErrorType} at {Source}", run.Id, run.Status, exception.GetType().Name, exception.StackTrace);
                    var current = await store.GetRunAsync(run.Id, stoppingToken);
                    if (current is { IsRunnable: true } && current.LeaseOwner == owner)
                    {
                        var safeReason = exception is ExecutorFailureException or ExecutorEvidenceException or PersistenceFailureException or ConfigurationException or InvalidOperationException or ArgumentException ? exception.Message : "Stage failed; inspect operational diagnostics using the run identifier.";
                        if (exception is TaskReconciliationException { Outcome: ExecutionOutcome.AwaitingHumanInput } && !safeReason.StartsWith("Human clarification required:", StringComparison.Ordinal)) safeReason = "Human clarification required: " + safeReason;
                        var failed = exception is TaskReconciliationException reconciliation ? current.Transition(RunStage.Paused, safeReason) with { Outcome = reconciliation.Outcome } : exception is ExecutorFailureException or ExecutorEvidenceException or PersistenceFailureException or ConfigurationException or ExecutionLimitException ? current.Transition(RunStage.Paused, safeReason) with { Outcome = ExecutionOutcome.Blocked } : current.Transition(RunStage.Failed, safeReason);
                        failed = failed with { FailureCategory = FailureDiagnosis.Category(exception, current.CurrentStage) };
                        if (await store.SaveRunAsync(failed, current.Version, owner, stoppingToken) && (exception is HttpRequestException or TimeoutException or TaskCanceledException) && current.RetriesFor(current.CurrentStage) < current.Policy.MaxRetries)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, current.RetriesFor(current.CurrentStage) + 1)), stoppingToken);
                            var retryable = await store.GetRunAsync(run.Id, stoppingToken);
                            if (retryable?.CurrentStage == RunStage.Failed && retryable.LeaseOwner == owner && retryable.Version == current.Version + 1) await store.SaveRunAsync(retryable.Transition(retryable.ResumeStage), retryable.Version, owner, stoppingToken);
                        }
                    }
                }
                finally { execution.Cancel(); await heartbeat; await store.ReleaseLeaseAsync(run.Id, owner, stoppingToken); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning("Worker unavailable: {ErrorType}", exception.GetType().Name); await Task.Delay(5000, stoppingToken); }
        }
    }
    private async Task HeartbeatAsync(string id, string owner, CancellationTokenSource execution)
    {
        try
        {
            while (!execution.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), execution.Token);
                var run = await store.GetRunAsync(id, execution.Token);
                if (run is not { IsRunnable: true } || run.LeaseOwner != owner || !await store.RenewLeaseAsync(id, owner, execution.Token)) { execution.Cancel(); return; }
            }
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested) { }
        catch { execution.Cancel(); }
    }
    private async Task ReconcileApprovalsAsync(CancellationToken ct)
    {
        var waiting = await store.RunsAsync(null, 0, 1000, ct, "AwaitingHumanApproval");
        foreach (var run in waiting)
        {
            var approvals = await store.RecordsAsync("approval", run.ProjectId, run.Id, null, 0, 0, 100, ct);
            var approval = approvals.LastOrDefault();
            if (approval?.Status is not ("Approved" or "Rejected")) continue;
            var updated = run.Transition(approval.Status == "Approved" ? RunStage.PreparingDelivery : RunStage.Cancelled);
            await store.SaveRunAsync(updated, run.Version, null, ct);
        }
    }
}
