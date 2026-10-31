using AIEngineeringPlatform.Domain;
using System.Text.Json;
namespace AIEngineeringPlatform.Application.RuntimeV2;

// Versioned communication mirrors existing orchestration facts. It never advances a stage or certifies a model assertion.
public sealed class WorkflowCommunication(IPlatformStore store)
{
    private static string Bound(string value, int maximum) => value.Length <= maximum ? value : value[..(maximum - 24)] + " [bounded; see original]";
    public async Task HandoffAsync(ExecutionRun run, AgentRole role, string taskId, AgentResult result, CancellationToken ct)
    {
        if (run.AgentRuntimeVersion != 2) return;
        var message = Message(run, role, taskId, EngineeringMessageKind.EngineeringHandoff, null,
            new EngineeringHandoffPayload(Bound(result.Summary, 4000), [], result.Decision is "BLOCKED" or "AWAITING_HUMAN_INPUT" ? [Bound(result.Summary, 2000)] : [], Bound(string.IsNullOrWhiteSpace(result.NextContext) ? "Consume actual tool results and verify acceptance criteria before completion." : result.NextContext, 4000)), [], [new(taskId, "task", null)]);
        await new DurableEngineeringMessages(store).PersistAsync(message, role.ToString(), new HashSet<string> { "Orchestrator" }, ct);
        if (result.Decision == "AWAITING_HUMAN_INPUT")
        {
            var clarification = Message(run, role, taskId, EngineeringMessageKind.ClarificationRequest, null, new ClarificationRequestPayload(Bound(result.Summary, 4000), [], true), [], [new(taskId, "task", null)]);
            await new DurableEngineeringMessages(store).PersistAsync(clarification, role.ToString(), new HashSet<string> { "Orchestrator" }, ct);
        }
    }
    public async Task ImplementationAsync(ExecutionRun run, AgentRole role, ToolResult diff, string operationId, CancellationToken ct)
    {
        if (run.AgentRuntimeVersion != 2) return;
        var evidence = diff.EvidenceId == null ? Array.Empty<EvidenceReference>() : [new EvidenceReference(operationId, operationId, diff.WorkspaceRevision)];
        var message = Message(run, role, operationId, EngineeringMessageKind.ImplementationResult, diff.WorkspaceRevision,
            new ImplementationResultPayload(!diff.Success ? "Blocked" : string.IsNullOrWhiteSpace(diff.Output) ? "NoOp" : "ProposedComplete", [], []), evidence, []);
        await new DurableEngineeringMessages(store).PersistAsync(message, role.ToString(), new HashSet<string> { "Orchestrator" }, ct);
    }
    public async Task TestReportAsync(ExecutionRun run, ToolResult result, string operationId, CancellationToken ct)
    {
        if (run.AgentRuntimeVersion != 2) return;
        var summary = ExecutorTestSummary.Read(result.Output);
        var count = summary?.ExecutedTests ?? 0;
        var passed = result.Success && result.ExitCode == 0 && summary?.ExitCode == 0 && count > 0;
        var evidence = result.EvidenceId == null ? Array.Empty<EvidenceReference>() : [new EvidenceReference(operationId, operationId, result.WorkspaceRevision)];
        var message = Message(run, AgentRole.TestEngineer, operationId, EngineeringMessageKind.TestReport, result.WorkspaceRevision,
            new TestReportPayload(passed ? "Passed" : "Failed", count, passed ? [] : ["Required verification failed or has no certified nonzero executed-test count. Inspect the original tool receipt."]), evidence, []);
        if (passed) await new RuntimeEvidenceResolver(store).ResolveAsync(message, ct);
        await new DurableEngineeringMessages(store).PersistAsync(message, AgentRole.TestEngineer.ToString(), new HashSet<string> { "Orchestrator" }, ct);
        if (result.Success && !passed) throw new TaskReconciliationException(ExecutionOutcome.Blocked, "V2 verification requires a nonzero executed-test count and exit status from the actual executor receipt. Preserve the source and reconcile the approved test profile.");
    }
    public async Task ReviewAsync(ExecutionRun run, AgentRole role, AgentResult result, bool approved, string? revision, CancellationToken ct)
    {
        if (run.AgentRuntimeVersion != 2) return;
        var taskId = PersistedRuntimeContextFrames.Hash(run.Id + "/review-message/" + run.TaskIndex + "/" + run.Iteration + "/" + role);
        var message = Message(run, role, taskId, EngineeringMessageKind.ReviewResult, revision,
            new ReviewResultPayload(approved ? "Approve" : "RequestChanges", result.Findings ?? [], approved ? [] : [Bound(result.Summary, 2000)]), await EvidenceAsync(run, revision, ct), []);
        await new DurableEngineeringMessages(store).PersistAsync(message, role.ToString(), new HashSet<string> { "Orchestrator" }, ct);
    }
    public async Task ApprovalAsync(ExecutionRun run, string approvalId, string description, CancellationToken ct)
    {
        if (run.AgentRuntimeVersion != 2) return;
        var message = Message(run, AgentRole.CodeReviewer, approvalId, EngineeringMessageKind.ApprovalRequest, null, new ApprovalRequestPayload("Delivery", approvalId, Bound(description, 4000)), [], []);
        message = message with { Sender = "Orchestrator", Recipient = "Owner" };
        await new DurableEngineeringMessages(store).PersistAsync(message, "Orchestrator", new HashSet<string> { "Owner" }, ct);
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
    private async Task<EvidenceReference[]> EvidenceAsync(ExecutionRun run, string? revision, CancellationToken ct)
    {
        var tools = await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 1000, ct);
        return tools.Where(x => x.Name is "RunTests" or "InspectDiff").Select(x => (Row: x, Result: JsonSerializer.Deserialize<ToolResult>(x.Content)))
            .Where(x => x.Result is { EvidenceId: not null } result && result.WorkspaceRevision == revision)
            .TakeLast(32).Select(x => new EvidenceReference(x.Row.Id, x.Row.Id, revision)).ToArray();
    }
    private static EngineeringMessage Message<T>(ExecutionRun run, AgentRole sender, string taskId, EngineeringMessageKind kind, string? revision, T payload, EvidenceReference[] evidence, ArtifactReference[] artifacts) =>
        new(1, kind, PersistedRuntimeContextFrames.Hash(taskId + "/v2/" + kind), run.ProjectId, run.Id, taskId, sender.ToString(), "Orchestrator", revision, run.CurrentTask?.Title ?? "Continue the assigned engineering task using recorded acceptance criteria", run.CurrentTask?.AcceptanceCriteria ?? ["Use actual source, test and review evidence for the assigned requirements"], artifacts, evidence, new(run.CurrentStage.ToString(), run.RetriesFor(run.CurrentStage), taskId), JsonSerializer.SerializeToElement(payload, EngineeringMessageContract.Json));
}
