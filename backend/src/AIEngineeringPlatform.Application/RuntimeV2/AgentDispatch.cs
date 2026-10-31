using AIEngineeringPlatform.Domain;
using System.Text.Json;

namespace AIEngineeringPlatform.Application.RuntimeV2;

public sealed class AgentRuntimeOptions
{
    // Applies only when creating a run. Existing runs retain their persisted generation.
    public bool EnableNewRuns { get; set; }
    public int MaxModelRequests { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 90;
    public bool IsValid => MaxModelRequests is >= 1 and <= 10 && TimeoutSeconds is >= 5 and <= 300;
}
public sealed record AgentDispatch(string Id, AgentRole Role, AgentRuntimeRequest Request, string Status, DateTimeOffset CreatedAt, DateTimeOffset Deadline, string? Worker = null, ModelExecutionResult? Result = null, string? Failure = null);
public interface IAgentDispatchStore
{
    Task<AgentDispatch?> GetAsync(string id, CancellationToken ct);
    Task<AgentDispatch> EnqueueAsync(AgentDispatch task, CancellationToken ct);
    Task<AgentDispatch?> ClaimAsync(AgentRole role, string worker, CancellationToken ct);
    Task FinishAsync(string id, string worker, ModelExecutionResult? result, string? failure, CancellationToken ct);
}

// Scheduling crosses a durable boundary. This component never executes a tool.
public sealed class DurableAgentDispatcher(IPlatformStore store, IAgentDispatchStore dispatches, IRuntimeContextFrames frames, DurableEngineeringMessages messages, AgentRuntimeOptions? options = null)
{
    public async Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
    {
        if (context.Run.AgentRuntimeVersion != 2 || context.Definition.Role is not (AgentRole.BackendEngineer or AgentRole.TestEngineer) || string.IsNullOrWhiteSpace(context.InvocationId)) throw new InvalidOperationException("Independent dispatch requires an authorized V2 role and stable invocation identity.");
        var id = PersistedRuntimeContextFrames.Hash(context.Run.Id + "/" + context.InvocationId + "/" + context.Run.RetriesFor(context.Run.CurrentStage));
        var existing = await dispatches.GetAsync(id, ct);
        if (existing == null)
        {
            var limits = options ?? new();
            if (!limits.IsValid) throw new ConfigurationException("Invalid independent agent runtime limits.");
            var remaining = context.Run.Policy.MaxBudgetUsd - context.Run.Cost;
            if (remaining <= 0 || context.Run.ToolCalls >= context.Run.Policy.MaxToolCalls) throw new ExecutionLimitException("Independent task has no remaining execution allowance.");
            var frame = await frames.SaveAsync(context.Project.Id, context.Run.Id, id, context.Context, ct);
            var purpose = context.Definition.Role == AgentRole.TestEngineer ? EngineeringTaskPurpose.Verification : EngineeringTaskPurpose.Implementation;
            // Bounded invocation, not a declaration that the feature is implemented or verified.
            var budget = new AgentExecutionBudget(limits.MaxModelRequests, context.Run.Policy.MaxToolCalls - context.Run.ToolCalls, context.Definition.MaxInputTokens, context.Definition.MaxOutputTokens, limits.TimeoutSeconds, remaining);
            var payload = new EngineeringTaskPayload(context.Definition.Role, purpose, purpose == EngineeringTaskPurpose.Implementation, context.Definition.AllowedTools, budget, [], frame.Id);
            var source = await LatestSourceAsync(context, ct);
            var objective = context.Run.CurrentTask?.Title ?? $"Perform the current {context.Run.CurrentStage} task using the persisted acceptance criteria and operational evidence";
            var criteria = context.Run.CurrentTask?.AcceptanceCriteria ?? ["Satisfy the assigned requirements using actual revision-bound evidence; model completion alone is not certification."];
            var message = new EngineeringMessage(1, EngineeringMessageKind.EngineeringTask, id, context.Project.Id, context.Run.Id, id, "Orchestrator", context.Definition.Role.ToString(), source, objective, criteria, [], [], new(context.Run.CurrentStage.ToString(), context.Run.RetriesFor(context.Run.CurrentStage), context.InvocationId), JsonSerializer.SerializeToElement(payload, EngineeringMessageContract.Json));
            // Immutable task/message and frame persist before a worker can claim the queue entry.
            await messages.PersistAsync(message, "Orchestrator", new HashSet<string> { context.Definition.Role.ToString() }, ct);
            var request = new AgentRuntimeRequest(message, context with { InvocationId = id }, frame.Id, frame.Sha256);
            existing = await dispatches.EnqueueAsync(new(id, context.Definition.Role, request, "Scheduled", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(limits.TimeoutSeconds + 30)), ct);
        }
        if (existing.Request.Task.RunId != context.Run.Id || existing.Request.Task.ProjectId != context.Project.Id || existing.Role != context.Definition.Role) throw new UnauthorizedAccessException("Independent task scope changed.");
        while (existing.Status is "Scheduled" or "Running")
        {
            await Task.Delay(100, ct);
            existing = await dispatches.GetAsync(id, ct) ?? throw new RuntimeReconciliationException("Independent task checkpoint is missing.");
        }
        if (existing.Status == "Completed" && existing.Result != null)
        {
            var authorized = (await store.AgentsAsync(ct)).SingleOrDefault(x => x.Role == existing.Role);
            if (authorized == null || JsonSerializer.Serialize(authorized) != JsonSerializer.Serialize(existing.Request.Context.Definition)) throw new TaskReconciliationException(ExecutionOutcome.Blocked, "Independent agent authorization changed; reconcile the frozen task before consuming its proposal.");
            return existing.Result;
        }
        throw new TaskReconciliationException(ExecutionOutcome.Blocked, "Independent agent task requires reconciliation: " + existing.Id + "; " + existing.Status + ". Preserve the invocation, usage receipts and source before any explicit retry.");
    }
    private async Task<string?> LatestSourceAsync(AgentContext context, CancellationToken ct)
    {
        string? revision = null;
        for (var offset = 0; ; offset += 1000)
        {
            var page = await store.RecordsAsync("tool", context.Project.Id, context.Run.Id, null, 0, offset, 1000, ct);
            foreach (var row in page)
            {
                var tool = JsonSerializer.Deserialize<ToolResult>(row.Content);
                if (tool?.Success == true && tool.WorkspaceRevision != null) revision = tool.WorkspaceRevision;
            }
            if (page.Count < 1000) return revision;
        }
    }
}

// One bounded model operation per claim. Repository tools remain exclusively in EngineeringWorkflow.
public sealed class IndependentAgentTaskProcessor(IPlatformStore store, IAgentDispatchStore dispatches, IAgentRuntime runtime)
{
    public async Task<bool> ProcessOneAsync(AgentRole role, string worker, CancellationToken ct)
    {
        if (role is not (AgentRole.BackendEngineer or AgentRole.TestEngineer)) throw new ArgumentException("Role has not migrated to independent dispatch.");
        var task = await dispatches.ClaimAsync(role, worker, ct);
        if (task == null) return false;
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var remaining = task.Deadline - DateTimeOffset.UtcNow;
        execution.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        var monitor = MonitorAsync(task, execution);
        try
        {
            await RequireOwnershipAsync(task, execution.Token);
            var currentDefinition = (await store.AgentsAsync(execution.Token)).SingleOrDefault(x => x.Role == task.Role);
            if (currentDefinition == null || JsonSerializer.Serialize(currentDefinition) != JsonSerializer.Serialize(task.Request.Context.Definition)) throw new UnauthorizedAccessException("Assigned role authorization changed; reconcile the frozen task before dispatch.");
            var result = await runtime.ExecuteAsync(task.Request, execution.Token);
            await RequireOwnershipAsync(task, execution.Token);
            await dispatches.FinishAsync(task.Id, worker, result.Response, null, execution.Token);
        }
        catch (Exception error)
        {
            // Never automatically requeue a claimed invocation: even cancellation can occur after provider completion.
            using var persist = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await dispatches.FinishAsync(task.Id, worker, null, error.GetType().Name, persist.Token);
        }
        finally { execution.Cancel(); await monitor; }
        return true;
    }
    private async Task RequireOwnershipAsync(AgentDispatch task, CancellationToken ct)
    {
        var run = await store.GetRunAsync(task.Request.Task.RunId, ct);
        var snapshot = task.Request.Context.Run;
        if (run == null || !run.IsRunnable || run.AgentRuntimeVersion != 2 || run.LeaseOwner != snapshot.LeaseOwner || run.LeaseExpiry <= DateTimeOffset.UtcNow || run.CurrentStage != snapshot.CurrentStage || run.Iteration != snapshot.Iteration || run.TaskIndex != snapshot.TaskIndex) throw new ConcurrencyException();
    }
    private async Task MonitorAsync(AgentDispatch task, CancellationTokenSource execution)
    {
        try
        {
            while (!execution.IsCancellationRequested)
            {
                await Task.Delay(500, execution.Token);
                await RequireOwnershipAsync(task, execution.Token);
            }
        }
        catch { execution.Cancel(); }
    }
}
