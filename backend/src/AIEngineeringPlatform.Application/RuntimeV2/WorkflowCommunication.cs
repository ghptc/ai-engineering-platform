using AIEngineeringPlatform.Domain;
using System.Text.Json;
namespace AIEngineeringPlatform.Application.RuntimeV2;

// Versioned communication mirrors existing orchestration facts. It never advances a stage or certifies a model assertion.
public sealed class WorkflowCommunication(IPlatformStore store)
{
    private static string Bound(string value, int maximum) => value.Length <= maximum ? value : value[..(maximum - 24)] + " [bounded; see original]";
    public async Task HandoffAsync(ExecutionRun run, AgentRole role, string taskId, AgentResult result, CancellationToken ct)
    {
        if (run.AgentRuntimeVersion != 2 || role is not (AgentRole.BackendEngineer or AgentRole.TestEngineer)) return;
        var message = Message(run, role, taskId, EngineeringMessageKind.EngineeringHandoff, null,
            new EngineeringHandoffPayload(Bound(result.Summary, 4000), [], result.Decision is "BLOCKED" or "AWAITING_HUMAN_INPUT" ? [Bound(result.Summary, 2000)] : [], Bound(string.IsNullOrWhiteSpace(result.NextContext) ? "Consume actual tool results and verify acceptance criteria before completion." : result.NextContext, 4000)), [], [new(taskId, "task", null)]);
        await new DurableEngineeringMessages(store).PersistAsync(message, role.ToString(), new HashSet<string> { "Orchestrator" }, ct);
    }
    public async Task ReworkAsync(ExecutionRun run, CancellationToken ct)
    {
        if (run.AgentRuntimeVersion != 2) return;
        var pending = run.ReworkPlan.Where(x => x.Iteration == run.Iteration + 1).GroupBy(x => (x.SourceAgent, x.WorkspaceRevision, x.Reason));
        var tools = await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 1000, ct);
        foreach (var group in pending)
        {
            var source = group.Key.WorkspaceRevision;
            var evidence = tools.Where(x => x.Name is "RunTests" or "InspectDiff")
                .Select(x => (Row: x, Result: JsonSerializer.Deserialize<ToolResult>(x.Content)))
                .Where(x => x.Result is { } result && result.WorkspaceRevision == source && result.EvidenceId != null)
                .TakeLast(32).Select(x => new EvidenceReference(x.Row.Id, x.Row.Id, source)).ToArray();
            if (evidence.Length == 0) throw new TaskReconciliationException(ExecutionOutcome.Blocked, "Rework requires persisted source-revision evidence; preserve findings and reconcile the missing receipt.");
            var taskId = PersistedRuntimeContextFrames.Hash(run.Id + "/rework/" + run.TaskIndex + "/" + (run.Iteration + 1) + "/" + group.Key.SourceAgent);
            var payload = new ReworkRequestPayload(group.Select(x => x.TargetAgent.ToString()).Distinct().ToArray(), Bound(group.Key.Reason, 4000), [Bound(group.Key.Reason, 2000)]);
            var message = Message(run, group.Key.SourceAgent, taskId, EngineeringMessageKind.ReworkRequest, source, payload, evidence, []);
            await new DurableEngineeringMessages(store).PersistAsync(message, group.Key.SourceAgent.ToString(), new HashSet<string> { "Orchestrator" }, ct);
        }
    }
    private static EngineeringMessage Message<T>(ExecutionRun run, AgentRole sender, string taskId, EngineeringMessageKind kind, string? revision, T payload, EvidenceReference[] evidence, ArtifactReference[] artifacts) =>
        new(1, kind, PersistedRuntimeContextFrames.Hash(taskId + "/v2/" + kind), run.ProjectId, run.Id, taskId, sender.ToString(), "Orchestrator", revision, run.CurrentTask?.Title ?? "Continue the assigned engineering task using recorded acceptance criteria", run.CurrentTask?.AcceptanceCriteria ?? ["Use actual source, test and review evidence for the assigned requirements"], artifacts, evidence, new(run.CurrentStage.ToString(), run.RetriesFor(run.CurrentStage), taskId), JsonSerializer.SerializeToElement(payload, EngineeringMessageContract.Json));
}
