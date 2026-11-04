namespace AIEngineeringPlatform.Domain;
public enum DependencyApprovalStatus { Pending, Resolving, ReadyForReview, Approved, Rejected, ChangesRequested, ResolutionFailed }
public sealed record DependencyPackage(string Name, string Version, bool Development, string Reason);
public sealed record DependencyProposal(int Version, string Ecosystem, string PackageManager, string Registry,
    DependencyPackage[] Packages, string[] Capabilities, string SecurityImplications, string[] Alternatives,
    string[] IndependentTasks, string[] BlockedTasks, string[] EvidenceReferences);
public sealed record ResolvedDependency(string Name, string Version, string Integrity, string Source, bool Direct, bool Development, string License, string SecurityStatus, bool HasInstallScript, string? InstallPath = null, string? Alias = null, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? CacheRequired = null, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] bool? RuntimeCached = null);
public sealed record DependencyAcquisitionEvidence(int RegistryRequests, long DownloadedBytes, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, int HelperExitCode);
public sealed record DependencyResolution(string Id, string ProjectId, string RunId, RepositoryConnection Repository, string ManifestBeforeSha256, string LockBeforeSha256, string Manifest, string Lock, string CacheSha256, ResolvedDependency[] Packages, string PlanSha256, string? ResolverImage = null, string ResolverContract = "npm-v1", DependencyAcquisitionEvidence? Acquisition = null);
public sealed record DependencyDecision(string Action, string Owner, DateTimeOffset At, string[] PackageIds, bool RegistryAccess, string Reason);
public sealed record DependencyApprovalRequest(string Id, string ProjectId, string RunId, RunStage Stage, AgentRole Agent,
    string OriginatingTaskId, string OriginatingOperationId, int TaskIndex, DependencyProposal Proposal,
    DependencyApprovalStatus Status, long Version, DateTimeOffset CreatedAt, DependencyDecision[] Audit,
    DependencyResolution? Resolution = null, string? Diagnostic = null, DateTimeOffset? ExecutionDeadline = null, string? ParentRequestId = null);
public sealed record DependencyGrant(string Id, string ProjectId, string RunId, RepositoryConnection Repository, string PlanSha256, string[] PackageIds, string Owner, DateTimeOffset ApprovedAt);

public sealed record DependencyRequestSummary(string Id, string ProjectId, string RunId, RunStage Stage, AgentRole Agent, int TaskIndex, DependencyApprovalStatus Status, long Version, DateTimeOffset CreatedAt, DependencyPackage[] Packages)
{
    public static DependencyRequestSummary From(DependencyApprovalRequest request) => new(request.Id, request.ProjectId, request.RunId, request.Stage, request.Agent, request.TaskIndex, request.Status, request.Version, request.CreatedAt, request.Proposal.Packages);
}
