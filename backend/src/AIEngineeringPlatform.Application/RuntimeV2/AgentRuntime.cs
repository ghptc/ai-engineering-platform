using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIEngineeringPlatform.Domain;

namespace AIEngineeringPlatform.Application.RuntimeV2;

public sealed record AgentRuntimeLimits(int MaxModelRequests, decimal MaxCostUsd);
public sealed record RuntimeContextFrame(string Id, string ProjectId, string RunId, string TaskId, string Sha256, string Content);
public sealed record AgentRuntimeRequest(EngineeringMessage Task, AgentContext Context, string ContextFrameId, string ContextSha256);
public sealed record AgentRuntimeResult(string Runtime, string TaskId, string ContextFrameId, ModelExecutionResult Response);
public interface IAgentRuntime
{
    string Name { get; }
    Task<AgentRuntimeResult> ExecuteAsync(AgentRuntimeRequest request, CancellationToken ct);
}
public interface IRuntimeContextFrames
{
    Task<RuntimeContextFrame> SaveAsync(string projectId, string runId, string taskId, string context, CancellationToken ct);
    Task<RuntimeContextFrame> GetAsync(string id, string projectId, string runId, CancellationToken ct);
}
public sealed class PersistedRuntimeContextFrames(IPlatformStore store) : IRuntimeContextFrames
{
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public async Task<RuntimeContextFrame> SaveAsync(string projectId, string runId, string taskId, string context, CancellationToken ct)
    {
        if (Encoding.UTF8.GetByteCount(context) > 1000000 || string.IsNullOrWhiteSpace(context)) throw new ArgumentException("Runtime context exceeds its bounded frame.");
        var hash = Hash(context); var id = Hash(projectId + "/" + runId + "/" + taskId + "/" + hash);
        var frame = new RuntimeContextFrame(id, projectId, runId, taskId, hash, context);
        await store.AppendAsync(new() { Id = id, Kind = "runtimeContextV2", ProjectId = projectId, RunId = runId, Name = "BoundedContextFrame", Content = JsonSerializer.Serialize(frame), Metadata = new() { ["sha256"] = hash, ["taskId"] = taskId } }, ct);
        return frame;
    }
    public async Task<RuntimeContextFrame> GetAsync(string id, string projectId, string runId, CancellationToken ct)
    {
        var record = await store.GetRecordAsync("runtimeContextV2", id, ct) ?? throw new KeyNotFoundException("Runtime context frame is unavailable.");
        if (record.ProjectId != projectId || record.RunId != runId) throw new UnauthorizedAccessException("Runtime context scope mismatch.");
        var frame = JsonSerializer.Deserialize<RuntimeContextFrame>(record.Content) ?? throw new InvalidOperationException("Invalid persisted runtime context.");
        if (frame.Id != id || frame.ProjectId != projectId || frame.RunId != runId || Hash(frame.Content) != frame.Sha256) throw new InvalidOperationException("Runtime context integrity check failed.");
        return frame;
    }
}
public sealed class DurableEngineeringMessages(IPlatformStore store)
{
    public async Task PersistAsync(EngineeringMessage message, string sender, IReadOnlySet<string> recipients, CancellationToken ct)
    {
        var validated = EngineeringMessageContract.Parse(EngineeringMessageContract.Serialize(message), sender, recipients);
        var run = await store.GetRunAsync(validated.RunId, ct) ?? throw new KeyNotFoundException("Execution is unavailable.");
        if (run.ProjectId != validated.ProjectId) throw new UnauthorizedAccessException("Engineering message execution scope mismatch.");
        await store.AppendAsync(new() { Id = validated.MessageId, Kind = "agentMessageV2", ProjectId = validated.ProjectId, RunId = validated.RunId, Name = validated.Kind.ToString(), Status = "Unverified", Content = EngineeringMessageContract.Serialize(validated), Metadata = new() { ["version"] = "1", ["sender"] = sender, ["recipient"] = validated.Recipient, ["taskId"] = validated.TaskId, ["sourceRevision"] = validated.SourceRevision ?? "" } }, ct);
    }
}
public sealed record VerifiedToolEvidence(string OperationId, string EvidenceId, string Operation, string? SourceRevision, bool Success, int? ExitCode, int ExecutedTests, string? Image);
public sealed class RuntimeEvidenceResolver(IPlatformStore store)
{
    public async Task<IReadOnlyList<VerifiedToolEvidence>> ResolveAsync(EngineeringMessage message, CancellationToken ct)
    {
        if (message.Evidence.Select(x => x.OperationId).Distinct().Count() != message.Evidence.Length || message.Evidence.Select(x => x.RecordId).Distinct().Count() != message.Evidence.Length) throw new InvalidOperationException("Duplicate evidence references cannot inflate verification.");
        var verified = new List<VerifiedToolEvidence>();
        foreach (var reference in message.Evidence)
        {
            var row = await store.GetRecordAsync("tool", reference.RecordId, ct) ?? throw new InvalidOperationException("Referenced tool evidence is missing.");
            if (row.ProjectId != message.ProjectId || row.RunId != message.RunId || row.Metadata.GetValueOrDefault("operationId", row.Id) != reference.OperationId) throw new UnauthorizedAccessException("Tool evidence scope or operation identity mismatch.");
            var result = JsonSerializer.Deserialize<ToolResult>(row.Content) ?? throw new InvalidOperationException("Invalid persisted tool receipt.");
            if (string.IsNullOrWhiteSpace(result.EvidenceId) || result.WorkspaceRevision != reference.WorkspaceRevision) throw new InvalidOperationException("Tool evidence revision mismatch.");
            var count = 0;
            if (row.Name == "RunTests")
            {
                var match = System.Text.RegularExpressions.Regex.Matches(result.Output, @"(?m)^Executor evidence: tests=(\d+); exit=0;").LastOrDefault();
                if (match?.Success == true && int.TryParse(match.Groups[1].Value, out var found)) count = found;
            }
            verified.Add(new(reference.OperationId, result.EvidenceId, row.Name, result.WorkspaceRevision, result.Success && row.Status == "Succeeded", result.ExitCode, count, result.Execution?.Image));
        }
        if (message.Kind == EngineeringMessageKind.TestReport)
        {
            var report = EngineeringMessageContract.Payload<TestReportPayload>(message);
            if (report.Outcome == "Passed" && (verified.Count == 0 || verified.Any(x => x.Operation != "RunTests" || !x.Success || x.ExitCode != 0 || x.ExecutedTests == 0 || x.SourceRevision != message.SourceRevision) || verified.Sum(x => x.ExecutedTests) != report.ExecutedCount)) throw new InvalidOperationException("A passing report requires nonzero executed tests from matching persisted receipts.");
        }
        return verified;
    }
}

public sealed record RuntimeInvocationCheckpoint(string Id, string Fingerprint, string ContextFrameId, string Status, DateTimeOffset StartedAt, DateTimeOffset Deadline, string Owner, ModelExecutionResult? Result, string? ErrorCategory, int Version = 1);
public sealed record RuntimeInvocationStart(bool Acquired, RuntimeInvocationCheckpoint Checkpoint);
public interface IRuntimeInvocationStore
{
    Task<RuntimeInvocationStart> BeginAsync(RuntimeInvocationCheckpoint proposed, CancellationToken ct);
    Task<RuntimeInvocationCheckpoint> GetAsync(string id, CancellationToken ct);
    Task CompleteAsync(string id, string owner, ModelExecutionResult result, CancellationToken ct);
    Task FailAsync(string id, string owner, string category, CancellationToken ct);
}
public sealed class RuntimeReconciliationException(string message) : InvalidOperationException(message);

public static class RuntimeInvocationIdentity
{
    public static string Fingerprint(AgentRuntimeRequest request) => PersistedRuntimeContextFrames.Hash(EngineeringMessageContract.Serialize(request.Task) + request.ContextSha256 + request.Context.Definition.Model + request.Context.Definition.InstructionVersion + request.Context.Definition.SystemInstructions);
}
