using AIEngineeringPlatform.Domain;
using System.Text.Json;
namespace AIEngineeringPlatform.Application;

public sealed record Page<T>(IReadOnlyList<T> Items, int Offset, int Limit);
public sealed record PlatformStatistics(long ActiveProjects, long RunningExecutions, long CompletedExecutions, long FailedExecutions, long PendingApprovals, long TokenUsage, decimal EstimatedCostUsd, long Runs);
public interface IPlatformStore
{
    Task InitializeAsync(CancellationToken ct);
    Task<PlatformStatistics> StatisticsAsync(CancellationToken ct);
    Task<IReadOnlyList<RecordEntry>> RecentActivityAsync(CancellationToken ct);
    Task<IReadOnlyList<RecordEntry>> FlushOutboxAsync(CancellationToken ct);
    Task<Project?> GetProjectAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<Project>> ProjectsAsync(int offset, int limit, CancellationToken ct);
    Task<bool> SaveProjectAsync(Project project, long? expectedVersion, CancellationToken ct);
    Task<ExecutionRun?> GetRunAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<ExecutionRun>> RunsAsync(string? projectId, int offset, int limit, CancellationToken ct, string? status = null);
    Task<ExecutionRun> CreateRunAsync(ExecutionRun run, CancellationToken ct);
    Task<ExecutionRun?> ClaimAsync(string owner, TimeSpan lease, CancellationToken ct);
    Task<bool> SaveRunAsync(ExecutionRun run, long expectedVersion, string? owner, CancellationToken ct);
    Task<bool> RenewLeaseAsync(string id, string owner, CancellationToken ct);
    Task ReleaseLeaseAsync(string id, string owner, CancellationToken ct);
    Task AppendAsync(RecordEntry entry, CancellationToken ct);
    Task<IReadOnlyList<RecordEntry>> RecordsAsync(string kind, string? projectId, string? runId, string? conversationId, long after, int offset, int limit, CancellationToken ct, string? status = null);
    Task<RecordEntry?> GetRecordAsync(string kind, string id, CancellationToken ct);
    Task<bool> DecideApprovalAsync(string id, bool approve, string reason, CancellationToken ct);
    Task<bool> IngestWebhookAsync(RecordEntry entry, CancellationToken ct);
    Task CompleteWebhookAsync(string id, string status, CancellationToken ct);
    Task SaveAgentAsync(AgentDefinition agent, CancellationToken ct);
    Task MarkRequirementsVerifiedAsync(string[] ids, string runId, string evidenceId, CancellationToken ct);
    Task<IReadOnlyList<AgentDefinition>> AgentsAsync(CancellationToken ct);
    Task<bool> PingAsync(CancellationToken ct);
}
public interface IExecutionPublisher { Task PublishAsync(RecordEntry entry, CancellationToken ct); }
public sealed record AgentContext(Project Project, ExecutionRun Run, string Context, AgentDefinition Definition);
public sealed record ToolCall(string Name, JsonElement Arguments);
public sealed record AgentResult(string Summary, string Decision, string[] AcceptanceCriteria, string[] Evidence, string[] Assumptions, ToolCall[] ToolCalls, string NextContext, RequirementProposal[]? Requirements = null, ReviewFinding[]? Findings = null, QualityCheck[]? Checks = null, string[]? OpenQuestions = null, ArchitectureDecision[]? ArchitectureDecisions = null, string[]? ReworkTargets = null, PlanningAssessment? Planning = null);
public sealed record AgentHandoff(string Summary, string CompletedWork, string[] RelevantArtifacts, QualityCheck[] ValidationResults, string[] Blockers, string NextAction);
public sealed record RequirementProposal(string Title, string Description, string UserStory, string[] AcceptanceCriteria, int Priority, string[] Dependencies, bool NonFunctional);
public sealed record ReviewFinding(string Severity, string File, int? Line, string Description, string SuggestedCorrection);
public sealed record QualityCheck(string RequirementId, string Scenario, bool Passed, string EvidenceId, string Details);
public sealed record ArchitectureDecision(string Title, string Context, string Decision, string Consequences, string Mermaid);
public sealed record ModelExecutionResult(AgentResult Output, string ResponseId, long InputTokens, long OutputTokens, decimal Cost, ProviderDiagnostics? Diagnostics = null);
public interface ILanguageModelProvider { Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct); }
public interface IEngineeringAgent { AgentRole Role { get; } Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct); }
public sealed record OperationExecution(string Profile, string[] Command, string WorkingDirectory, string Stdout, string Stderr);
public sealed record ToolResult(bool Success, string Output, string? EvidenceId = null, string? WorkspaceRevision = null, int? ExitCode = null, OperationExecution? Execution = null);
public sealed class SandboxExecutionLimits { public int TimeoutSeconds { get; set; } = 120; public int CpuLimit { get; set; } = 1; public int MemoryMb { get; set; } = 1024; public int OutputLimitBytes { get; set; } = 100000; }
public sealed record SandboxRequest(string OperationId, string RunId, RepositoryConnection Repository, string Operation, JsonElement Arguments, int TimeoutSeconds = 60, int CpuLimit = 1, int MemoryMb = 512, int OutputLimitBytes = 100000, bool NetworkEnabled = false, long Fence = 0, string? LeaseOwner = null, int MaxTransientRetries = 0);
public sealed record SandboxResponse(bool Success, string Output, string EvidenceId, int? ExitCode, string WorkspaceId, string? DiffHash = null, OperationExecution? Execution = null);
public interface IExecutionSandbox { bool Configured { get; } Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(Configured); Task<SandboxResponse> ExecuteAsync(SandboxRequest request, CancellationToken ct); }
public interface IAgentTool { string Name { get; } Task<ToolResult> ExecuteAsync(AgentContext context, JsonElement arguments, string operationId, CancellationToken ct); }
public interface ISourceControl
{
    bool Configured { get; }
    Task<string> ExecuteAsync(RepositoryConnection repository, string operation, JsonElement arguments, string operationId, CancellationToken ct);
    bool VerifyWebhook(ReadOnlySpan<byte> payload, string signature);
}
public sealed class ConcurrencyException() : Exception("The resource changed; reload it and retry.");
public sealed class ConfigurationException(string message) : Exception(message);

public sealed class TaskReconciliationException(ExecutionOutcome outcome, string message) : InvalidOperationException(message) { public ExecutionOutcome Outcome { get; } = outcome; }

public sealed record PlanningAssessment(string[] Risks, string[] Prerequisites, PlanningBlocker[] Blockers, string BootstrapStrategy, string DependencyStrategy, string TestStrategy);
public sealed record PlanningBlocker(string Stage, string Reason, string Evidence);
