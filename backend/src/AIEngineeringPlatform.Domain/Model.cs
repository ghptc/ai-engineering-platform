namespace AIEngineeringPlatform.Domain;

public enum RunStage { Pending, Analyzing, Planning, PreparingContext, Implementing, GeneratingTests, RunningTests, Validating, Reviewing, AwaitingHumanApproval, PreparingDelivery, Completed, Failed, Paused, Cancelled }
public enum AgentRole { BusinessAnalyst, Architect, BackendEngineer, FrontendEngineer, TestEngineer, QAEngineer, CodeReviewer }
public enum ArtifactType { Requirements, ArchitectureDecision, DevelopmentContext, CodeDiff, TestReport, ValidationReport, CodeReview, PullRequest, Commit, ExecutionLog, NextContext }
public sealed record ExecutionPolicy(int MaxIterations = 5, int MaxToolCalls = 30, int MaxDurationMinutes = 60, decimal MaxBudgetUsd = 5, int MaxRetries = 2, bool RequireHumanApprovalForMerge = true, bool ReviewBeforeCommit = true)
{
    public void Validate()
    {
        if (MaxIterations is < 1 or > 20 || MaxToolCalls is < 1 or > 200 || MaxDurationMinutes is < 1 or > 240 || MaxBudgetUsd is <= 0 or > 1000 || MaxRetries is < 0 or > 5) throw new ArgumentException("Execution limits are outside supported bounds.");
        if (!RequireHumanApprovalForMerge) throw new ArgumentException("Automatic merge is not enabled in this release.");
    }
}
public sealed record RepositoryConnection(string Owner, string Repository, long InstallationId, string DefaultBranch = "main");
public sealed record Project
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string OriginalContext { get; init; } = "";
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
        ExecutionPolicy.Validate();
    }
}
public sealed record ExecutionRun
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; init; } = "";
    public string IdempotencyKey { get; init; } = "";
    public string[] RequirementIds { get; init; } = [];
    public Project? ProjectSnapshot { get; init; }
    public RunStage CurrentStage { get; init; } = RunStage.Pending;
    public RunStage ResumeStage { get; init; } = RunStage.Pending;
    public string Status => CurrentStage.ToString();
    public int Iteration { get; init; } = 1;
    public int RetryCount { get; init; }
    public ExecutionPolicy Policy { get; init; } = new();
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
    public string? FailureReason { get; init; }
    public decimal Cost { get; init; }
    public long TokenUsage { get; init; }
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
        if (resume && CurrentStage == RunStage.Failed && RetryCount >= Policy.MaxRetries) throw new InvalidOperationException("Maximum retries reached.");
        return this with
        {
            CurrentStage = target,
            ResumeStage = control ? CurrentStage : ResumeStage,
            FailureReason = reason,
            Iteration = Iteration + (correction ? 1 : 0),
            RetryCount = RetryCount + (resume && CurrentStage == RunStage.Failed ? 1 : 0),
            CompletedAt = target is RunStage.Completed or RunStage.Cancelled or RunStage.Failed ? DateTimeOffset.UtcNow : null
        };
    }
    public void CheckLimits(DateTimeOffset now)
    {
        if (now - StartedAt > TimeSpan.FromMinutes(Policy.MaxDurationMinutes)) throw new InvalidOperationException("Execution duration exhausted.");
        if (Cost >= Policy.MaxBudgetUsd) throw new InvalidOperationException("Execution budget exhausted.");
        if (ToolCalls >= Policy.MaxToolCalls) throw new InvalidOperationException("Tool invocation limit reached.");
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
