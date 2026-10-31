namespace AIEngineeringPlatform.Domain;

public sealed class ExecutionLimitException(string message) : InvalidOperationException(message);
public enum RunStage { Pending, Analyzing, Planning, PreparingContext, Implementing, GeneratingTests, RunningTests, Validating, Reviewing, AwaitingHumanApproval, PreparingDelivery, Completed, Failed, Paused, Cancelled }
public enum ExecutionOutcome { Implemented, AlreadySatisfied, AwaitingHumanInput, Blocked, Failed }
public enum AgentRole { BusinessAnalyst, Architect, BackendEngineer, FrontendEngineer, TestEngineer, QAEngineer, CodeReviewer }
public enum ArtifactType { Requirements, ArchitectureDecision, DevelopmentContext, CodeDiff, TestReport, ValidationReport, CodeReview, PullRequest, Commit, ExecutionLog, NextContext }
public sealed record ExecutionPolicy(int MaxIterations = 5, int MaxToolCalls = 30, int MaxDurationMinutes = 60, decimal MaxBudgetUsd = 5, int MaxRetries = 2, bool RequireHumanApprovalForMerge = true, bool ReviewBeforeCommit = true, Dictionary<AgentRole, int>? AgentToolBudgets = null)
{
    public void Validate()
    {
        if (MaxIterations is < 1 or > 20 || MaxToolCalls is < 1 or > 1000 || MaxDurationMinutes is < 1 or > 240 || MaxBudgetUsd is <= 0 or > 1000 || MaxRetries is < 0 or > 5) throw new ArgumentException("Execution limits are outside supported bounds.");
        if (AgentToolBudgets?.Any(x => !Enum.IsDefined(x.Key) || x.Value is < 1 or > 1000) == true) throw new ArgumentException("Agent tool budgets must be 1–1000.");
        if (!RequireHumanApprovalForMerge) throw new ArgumentException("Automatic merge is not enabled in this release.");
    }
}
public static class ExecutionPresets
{
    public static ExecutionPolicy QuickFix => new(3, 30, 30, 2, 1);
    public static ExecutionPolicy StandardDevelopment => new(5, 100, 90, 5, 2);
    public static ExecutionPolicy ExtendedAutonomousDevelopment => new(10, 300, 180, 10, 3);
}
public sealed record LimitExtension(ExecutionPolicy Previous, ExecutionPolicy Approved, string Reason, DateTimeOffset Timestamp);
public sealed record AgentUsage(AgentRole Agent, int TaskIndex, int ModelRequests = 0, int ToolInvocations = 0, int FailedToolInvocations = 0, int RetriedToolInvocations = 0);
public sealed record RepositoryConnection(string Owner, string Repository, long InstallationId, string DefaultBranch = "main");
public sealed record EngineeringTask(string Title, string Description, string[] AcceptanceCriteria, bool RequiresBusinessAnalysis = false, bool RequiresArchitecture = false, AgentRole[]? Developers = null, string[]? RequirementIds = null, string[]? HumanInstructionIds = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 120 || string.IsNullOrWhiteSpace(Description) || Description.Length > 12000 || AcceptanceCriteria is not { Length: > 0 and <= 20 } || AcceptanceCriteria.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 1000)) throw new ArgumentException("A focused task needs a title, bounded description and 1–20 acceptance criteria.");
        if (Developers is { Length: > 2 } || Developers?.Any(x => x is not (AgentRole.BackendEngineer or AgentRole.FrontendEngineer)) == true || RequirementIds is { Length: > 20 } || HumanInstructionIds is { Length: > 20 } || RequirementIds?.Any(string.IsNullOrWhiteSpace) == true || HumanInstructionIds?.Any(string.IsNullOrWhiteSpace) == true) throw new ArgumentException("Task specialists or references are outside supported bounds.");
    }
    public AgentRole[] Plan => [.. RequiresBusinessAnalysis ? new[] { AgentRole.BusinessAnalyst } : [], .. RequiresArchitecture ? new[] { AgentRole.Architect } : [], .. Developers is { Length: > 0 } ? Developers.Distinct() : [AgentRole.BackendEngineer], AgentRole.TestEngineer, AgentRole.QAEngineer, AgentRole.CodeReviewer];
}
public sealed record Project
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string OriginalContext { get; init; } = "";
    public string StandingInstructions { get; init; } = "";
    public string[] TechnologyStack { get; init; } = [];
    public RepositoryConnection? RepositoryConnection { get; init; }
    public string DefaultBranch { get; init; } = "main";
    public ExecutionPolicy ExecutionPolicy { get; init; } = new();
    public string Status { get; init; } = "Active";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public long Version { get; init; }
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || OriginalContext is null || OriginalContext.Length > 100_000 || Description is null || Description.Length > 10_000 || TechnologyStack is null || TechnologyStack.Length > 30) throw new ArgumentException("Project name is required (maximum 120 characters); context maximum is 100000 characters.");
        if (ExecutionPolicy is null) throw new ArgumentException("Execution policy is required.");
        if (StandingInstructions is null || StandingInstructions.Length > 12000) throw new ArgumentException("Project instructions must be at most 12000 characters.");
        ExecutionPolicy.Validate();
    }
}
public sealed record AgentTransition(AgentRole SourceAgent, AgentRole TargetAgent, int TaskIndex, int Iteration, DateTimeOffset Timestamp);
public sealed record ReworkTransition(AgentRole SourceAgent, AgentRole TargetAgent, string Reason, string[] FindingIds, string TaskId, int Iteration, string? WorkspaceRevision, DateTimeOffset Timestamp);
public sealed record ExecutionRun
{
    public int AgentRuntimeVersion { get; init; } = 1;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; init; } = "";
    public string IdempotencyKey { get; init; } = "";
    public string[] RequirementIds { get; init; } = [];
    public EngineeringTask[] Tasks { get; init; } = [];
    public int TaskIndex { get; init; }
    public EngineeringTask? CurrentTask => Tasks.ElementAtOrDefault(TaskIndex);
    public AgentRole[] SelectedAgents => CurrentTask?.Plan ?? Enum.GetValues<AgentRole>();
    public Project? ProjectSnapshot { get; init; }
    public RunStage CurrentStage { get; init; } = RunStage.Pending;
    public RunStage ResumeStage { get; init; } = RunStage.Pending;
    public string Status => CurrentStage.ToString();
    public int Iteration { get; init; } = 1;
    public Dictionary<RunStage, int> StageRetries { get; init; } = [];
    public int RetriesFor(RunStage stage) => StageRetries.TryGetValue(stage, out var count) ? count : StageRetries.Count == 0 ? RetryCount : 0;
    public int RetryCount { get; init; }
    public ExecutionPolicy Policy { get; init; } = new();
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
    public string? FailureCategory { get; init; }
    public string? FailureReason { get; init; }
    public ExecutionOutcome? Outcome { get; init; }
    public AgentTransition[] AgentTransitions { get; init; } = [];
    public ReworkTransition[] ReworkPlan { get; init; } = [];
    public bool NoSourceChanges { get; init; }
    public decimal Cost { get; init; }
    public long TokenUsage { get; init; }
    public AgentUsage[] Usage { get; init; } = [];
    public LimitExtension[] LimitExtensions { get; init; } = [];
    public ExecutionRun ExtendLimits(ExecutionPolicy policy, string reason, DateTimeOffset now)
    {
        policy.Validate();
        if (CurrentStage is RunStage.Completed or RunStage.Cancelled || string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw new ArgumentException("An active execution and bounded extension reason are required.");
        if (policy.MaxToolCalls < Policy.MaxToolCalls || policy.MaxIterations < Policy.MaxIterations || policy.MaxRetries < Policy.MaxRetries || policy.MaxDurationMinutes < Policy.MaxDurationMinutes || policy.MaxBudgetUsd < Policy.MaxBudgetUsd || policy.ReviewBeforeCommit != Policy.ReviewBeforeCommit) throw new ArgumentException("An extension may only increase limits; approval policies cannot change.");
        return this with { Policy = policy, LimitExtensions = [.. LimitExtensions, new(Policy, policy, reason, now)] };
    }
    public int ToolCalls { get; init; }
    public long Version { get; init; }
    public string? LeaseOwner { get; init; }
    public DateTimeOffset LeaseExpiry { get; init; }
    public bool IsRunnable => CurrentStage is not (RunStage.Completed or RunStage.Failed or RunStage.Paused or RunStage.Cancelled or RunStage.AwaitingHumanApproval);
    public ExecutionRun Transition(RunStage target, string? reason = null)
    {
        var control = IsRunnable && target is RunStage.Paused or RunStage.Cancelled or RunStage.Failed;
        var resume = CurrentStage is RunStage.Paused or RunStage.Failed && target == ResumeStage;
        var cancel = CurrentStage is RunStage.Paused or RunStage.Failed or RunStage.AwaitingHumanApproval && target == RunStage.Cancelled;
        var forward = (CurrentStage, target) switch
        {
            (RunStage.Pending, RunStage.Planning) when CurrentTask is { RequiresBusinessAnalysis: false, RequiresArchitecture: true } => true,
            (RunStage.Pending, RunStage.Implementing) when CurrentTask is { RequiresBusinessAnalysis: false, RequiresArchitecture: false } => true,
            (RunStage.Analyzing, RunStage.PreparingContext) when CurrentTask is { RequiresArchitecture: false } => true,
            (RunStage.Implementing, RunStage.RunningTests) when NoSourceChanges => true,
            (RunStage.Reviewing, RunStage.Completed) when NoSourceChanges => true,
            (RunStage.Pending, RunStage.Analyzing) or (RunStage.Analyzing, RunStage.Planning) or
            (RunStage.Planning, RunStage.PreparingContext) or (RunStage.PreparingContext, RunStage.Implementing) or
            (RunStage.Implementing, RunStage.GeneratingTests) or (RunStage.GeneratingTests, RunStage.RunningTests) or
            (RunStage.RunningTests, RunStage.Validating) or (RunStage.Validating, RunStage.Reviewing) or
            (RunStage.Reviewing, RunStage.AwaitingHumanApproval) or (RunStage.AwaitingHumanApproval, RunStage.PreparingDelivery) or
            (RunStage.PreparingDelivery, RunStage.Completed) => true,
            (RunStage.RunningTests or RunStage.Validating or RunStage.Reviewing or RunStage.PreparingDelivery, RunStage.Implementing) => true,
            _ => false
        };
        if (!control && !resume && !cancel && !forward) throw new InvalidOperationException($"Transition {CurrentStage} → {target} is not permitted.");
        var correction = target == RunStage.Implementing && CurrentStage is RunStage.RunningTests or RunStage.Validating or RunStage.Reviewing or RunStage.PreparingDelivery;
        if (correction && Iteration >= Policy.MaxIterations) throw new InvalidOperationException("Maximum development iterations reached.");
        if (resume && CurrentStage == RunStage.Failed && RetriesFor(ResumeStage) >= Policy.MaxRetries) throw new InvalidOperationException("Maximum retries reached.");
        return this with
        {
            CurrentStage = target,
            ResumeStage = control ? CurrentStage : ResumeStage,
            FailureReason = reason,
            FailureCategory = resume ? null : FailureCategory,
            Outcome = target == RunStage.Failed ? ExecutionOutcome.Failed : resume ? null : Outcome,
            Iteration = Iteration + (correction ? 1 : 0),
            StageRetries = resume && CurrentStage == RunStage.Failed ? new(StageRetries) { [ResumeStage] = RetriesFor(ResumeStage) + 1 } : StageRetries,
            RetryCount = RetryCount + (resume && CurrentStage == RunStage.Failed ? 1 : 0),
            CompletedAt = target is RunStage.Completed or RunStage.Cancelled or RunStage.Failed ? DateTimeOffset.UtcNow : null
        };
    }
    public ExecutionRun AdvanceTask()
    {
        if (CurrentStage != RunStage.PreparingDelivery && !(CurrentStage == RunStage.Reviewing && NoSourceChanges) || TaskIndex + 1 >= Tasks.Length || Iteration >= Policy.MaxIterations) throw new InvalidOperationException("No further authorized task fits this execution's iteration budget.");
        return this with { TaskIndex = TaskIndex + 1, Iteration = Iteration + 1, CurrentStage = RunStage.Pending, Outcome = null, NoSourceChanges = false, ReworkPlan = [], RequirementIds = Tasks[TaskIndex + 1].RequirementIds ?? [], FailureReason = null };
    }
    public void CheckLimits(DateTimeOffset now)
    {
        if (now - StartedAt > TimeSpan.FromMinutes(Policy.MaxDurationMinutes)) throw new ExecutionLimitException("Execution duration exhausted. Extend limits explicitly before resuming.");
        if (Cost >= Policy.MaxBudgetUsd) throw new ExecutionLimitException("Execution budget exhausted. Approve a budget extension before resuming.");
        if (ToolCalls >= Policy.MaxToolCalls) throw new ExecutionLimitException("Tool invocation limit reached. Checkpoints saved; extend limits before resuming.");
    }
}
public sealed record Requirement(string Id, string ProjectId, string Title, string Description, string[] AcceptanceCriteria, int Priority, string[] Dependencies, string Status, string AssignedRole, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AgentDefinition(string Id, AgentRole Role, string Name, string Description, string Model, string SystemInstructions, string[] Capabilities, string[] AllowedTools, bool Enabled = true, int InstructionVersion = 1, int MaxOutputTokens = 4000, decimal InputCostPerMillion = 0, decimal OutputCostPerMillion = 0, int ModelContextTokens = 128000, int MaxInputTokens = 96000);
public sealed record RecordEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Kind { get; init; } = "";
    public string ProjectId { get; init; } = "";
    public string? RunId { get; init; }
    public string? ConversationId { get; init; }
    public string Name { get; init; } = "";
    public string Content { get; init; } = "";
    public string Status { get; init; } = "";
    public string? ExternalId { get; init; }
    public long Sequence { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> Metadata { get; init; } = new();
}
