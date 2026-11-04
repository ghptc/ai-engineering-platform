using AIEngineeringPlatform.Domain;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace AIEngineeringPlatform.Application.Dependencies;
public interface IDependencyApprovalStore
{
    Task<DependencyApprovalRequest?> GetAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<DependencyRequestSummary>> ListAsync(string? projectId, string? runId, CancellationToken ct);
    Task<bool> SaveAsync(DependencyApprovalRequest request, long? expectedVersion, CancellationToken ct);
}
public interface IDependencyExecutor
{
    Task<DependencyResolution> ResolveAsync(DependencyApprovalRequest request, RepositoryConnection repository, CancellationToken ct);
    Task<DependencyResolution?> ResolutionAsync(string id, CancellationToken ct);
    Task<DependencyResolution?> ReconcileAsync(string id, CancellationToken ct);
    Task GrantAsync(DependencyGrant grant, CancellationToken ct);
}
public sealed class DependencyApprovalPendingException(string requestId) : InvalidOperationException("Dependency approval required: " + requestId) { }
public sealed partial class DependencyGovernance(IDependencyApprovalStore approvals, IPlatformStore store, IDependencyExecutor executor)
{
    public static readonly JsonSerializerOptions StrictJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 10 };
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static void Validate(DependencyProposal proposal)
    {
        if (proposal.Version != 1 || proposal.Ecosystem != "npm" || proposal.PackageManager != "npm" || proposal.Registry != "https://registry.npmjs.org" || proposal.Packages is not { Length: > 0 and <= 20 } || proposal.Capabilities is null || proposal.Alternatives is null || proposal.IndependentTasks is null || proposal.BlockedTasks is null || proposal.EvidenceReferences is null || proposal.SecurityImplications is null || proposal.SecurityImplications.Length > 4000) throw new ArgumentException("Dependency contract v1 requires npm, the official npm registry and 1–20 exact packages. Other ecosystems/sources require a separately supported adapter.");
        foreach (var values in new[] { proposal.Capabilities, proposal.Alternatives, proposal.IndependentTasks, proposal.BlockedTasks, proposal.EvidenceReferences })
            if (values.Length > 20 || values.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 1000)) throw new ArgumentException("Dependency references and metadata exceed bounds.");
        if (proposal.Packages.Any(x => x == null)) throw new ArgumentException("Package entries are required.");
        if (proposal.Packages.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != proposal.Packages.Length) throw new ArgumentException("Dependency names must be distinct.");
        foreach (var package in proposal.Packages)
            if (!PackageName().IsMatch(package.Name ?? "") || !ExactVersion().IsMatch(package.Version ?? "") || (package.Version?.Length ?? 0) > 80 || string.IsNullOrWhiteSpace(package.Reason) || package.Reason.Length > 2000) throw new ArgumentException("Use exact npm names/semantic versions with a justification. URLs, aliases, ranges and flags are not authorized.");
    }
    public async Task<DependencyApprovalRequest> RequestAsync(AgentContext context, JsonElement arguments, string operationId, CancellationToken ct)
    {
        if (arguments.GetRawText().Length > 32000 || arguments.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != arguments.EnumerateObject().Count()) throw new ArgumentException("Dependency request exceeds bounds or contains duplicate properties.");
        RejectDuplicateProperties(arguments);
        var proposal = arguments.Deserialize<DependencyProposal>(StrictJson) ?? throw new ArgumentException("Dependency proposal is required."); Validate(proposal);
        if (context.Project.RepositoryConnection == null) throw new ConfigurationException("Dependency requests require an authorized connected repository.");
        var id = Hash(context.Project.Id + "/" + context.Run.Id + "/" + context.Run.CurrentStage + "/" + context.Run.TaskIndex + "/" + JsonSerializer.Serialize(proposal.Packages.OrderBy(x => x.Name).Select(x => new { x.Name, x.Version, x.Development })));
        var existing = await approvals.GetAsync(id, ct); string? parentRequestId = null;
        if (existing?.Status == DependencyApprovalStatus.ChangesRequested && JsonSerializer.Serialize(existing.Proposal) != JsonSerializer.Serialize(proposal))
        {
            parentRequestId = existing.Id;
            id = Hash(id + "/revision/" + existing.Version + "/" + JsonSerializer.Serialize(proposal));
            existing = await approvals.GetAsync(id, ct);
        }
        if (existing == null && (await approvals.ListAsync(context.Project.Id, context.Run.Id, ct)).Count >= 50) throw new ExecutionLimitException("Dependency request capacity reached; preserve existing decisions for reconciliation.");
        var request = existing ?? new DependencyApprovalRequest(id, context.Project.Id, context.Run.Id, context.Run.CurrentStage, context.Definition.Role, context.InvocationId ?? operationId, operationId, context.Run.TaskIndex, proposal, DependencyApprovalStatus.Pending, 0, DateTimeOffset.UtcNow, [], ExecutionDeadline: context.Run.StartedAt.AddMinutes(context.Run.Policy.MaxDurationMinutes), ParentRequestId: parentRequestId);
        if (existing == null && !await approvals.SaveAsync(request, null, ct)) request = (await approvals.GetAsync(id, ct))!;
        if (context.Run.AgentRuntimeVersion == 2)
        {
            var message = new RuntimeV2.EngineeringMessage(1, RuntimeV2.EngineeringMessageKind.DependencyApprovalRequest,
                Hash(id + "/message"), request.ProjectId, request.RunId, request.OriginatingTaskId, request.Agent.ToString(), "Orchestrator", null,
                "Request owner authorization for exact dependencies; no permissions conferred.", ["Owner reviews exact package graph and explicitly authorizes any registry acquisition."], [], [],
                new(request.Stage.ToString(), context.Run.Iteration, request.Id),
                JsonSerializer.SerializeToElement(new RuntimeV2.DependencyApprovalPayload(request.Id, request.Proposal), RuntimeV2.EngineeringMessageContract.Json));
            await new RuntimeV2.DurableEngineeringMessages(store).PersistAsync(message, request.Agent.ToString(), new HashSet<string> { "Orchestrator" }, ct);
        }
        if (existing == null) await EventAsync(request, "DependencyApprovalRequired", ct); return request;
    }
    private Task EventAsync(DependencyApprovalRequest request, string name, CancellationToken ct) => store.AppendAsync(new() { Id = request.Id + "/" + request.Version + "/" + name, Kind = "event", ProjectId = request.ProjectId, RunId = request.RunId, Name = name, Content = JsonSerializer.Serialize(new { request.Id, request.Status, request.Version }) }, ct);
    public async Task<DependencyApprovalRequest> DecideAsync(string id, long version, string action, string[] packageIds, bool registryAccess, string reason, string owner, CancellationToken ct)
    {
        var request = await approvals.GetAsync(id, ct) ?? throw new KeyNotFoundException("Dependency request not found.");
        if (request.Status == DependencyApprovalStatus.Approved) throw new InvalidOperationException("This grant is final; use a separately reviewed dependency change rather than rewriting its approval.");
        if (request.Version != version) throw new ConcurrencyException();
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(reason) || reason.Length > 2000 || packageIds == null || packageIds.Length > 500 || packageIds.Distinct().Count() != packageIds.Length) throw new ArgumentException("A bounded owner decision and distinct package identities are required.");
        var run = await store.GetRunAsync(request.RunId, ct) ?? throw new KeyNotFoundException("Execution not found.");
        if (run.ProjectId != request.ProjectId || run.CurrentStage != RunStage.AwaitingDependencyApproval || run.ResumeStage != request.Stage || run.TaskIndex != request.TaskIndex) throw new InvalidOperationException("The request is not waiting at this execution checkpoint.");
        var project = await store.GetProjectAsync(request.ProjectId, ct) ?? throw new KeyNotFoundException();
        if (project.RepositoryConnection == null || (run.ProjectSnapshot?.RepositoryConnection is { } originalRepository && project.RepositoryConnection != originalRepository)) throw new InvalidOperationException("Repository scope changed; preserve the request for reconciliation.");
        if (action is "Resolve" or "RetryResolution") run.CheckLimits(DateTimeOffset.UtcNow);
        var decision = new DependencyDecision(action, owner, DateTimeOffset.UtcNow, packageIds, registryAccess, reason);
        DependencyApprovalStatus status;
        if (action is "Reject" or "RequestChanges") status = action == "Reject" ? DependencyApprovalStatus.Rejected : DependencyApprovalStatus.ChangesRequested;
        else if ((action == "Resolve" && request.Status is DependencyApprovalStatus.Pending or DependencyApprovalStatus.ResolutionFailed) && registryAccess) status = DependencyApprovalStatus.Resolving;
        else if (action == "Reconcile" && request.Status == DependencyApprovalStatus.Resolving) status = DependencyApprovalStatus.Resolving;
        else if (action == "Approve" && request.Status == DependencyApprovalStatus.ReadyForReview && !registryAccess)
        {
            var required = request.Resolution!.Packages.Select(x => x.Name + "@" + x.Version).Distinct().ToArray();
            if (packageIds.Except(required).Any()) throw new ArgumentException("A decision cannot broaden the resolved dependency graph.");
            packageIds = request.Audit.Where(x => x.Action == "Approve").SelectMany(x => x.PackageIds).Concat(packageIds).Distinct().ToArray();
            status = required.Except(packageIds).Any() ? DependencyApprovalStatus.ReadyForReview : DependencyApprovalStatus.Approved;
        }
        else throw new ArgumentException("Invalid dependency decision. Registry resolution requires explicit separate network authorization.");
        var updated = request with { ExecutionDeadline = run.StartedAt.AddMinutes(run.Policy.MaxDurationMinutes), Status = status, Version = version + 1, Audit = [.. request.Audit, decision], Diagnostic = null };
        if (updated.Audit.Length > 100) throw new InvalidOperationException("Decision history limit reached; preserve request for reconciliation.");
        if (!await approvals.SaveAsync(updated, version, ct)) throw new ConcurrencyException();
        await EventAsync(updated, "DependencyOwnerDecision", ct);
        if (action is "Resolve" or "RetryResolution" or "Reconcile")
        {
            try
            {
                var resolution = action == "Reconcile" ? await executor.ReconcileAsync(id, ct) ?? throw new InvalidOperationException("No completed resolution receipt exists. Do not blindly replay a potentially running acquisition.") : await executor.ResolveAsync(updated, project.RepositoryConnection, ct);
                ValidateResolution(updated, project.RepositoryConnection, resolution);
                var ready = updated with { Resolution = resolution, Status = DependencyApprovalStatus.ReadyForReview, Version = updated.Version + 1 };
                if (!await approvals.SaveAsync(ready, updated.Version, ct)) throw new ConcurrencyException(); updated = ready;
            }
            catch (Exception error) when (error is not OperationCanceledException && error is not ConcurrencyException)
            {
                // An ambiguous acquisition remains Resolving and can only be reconciled by an explicit owner read.
                var known = error is DependencyAcquisitionException { Failure.OutcomeKnown: true };
                var blocked = updated with { Status = known ? DependencyApprovalStatus.ResolutionFailed : DependencyApprovalStatus.Resolving, Diagnostic = error is DependencyAcquisitionException acquisition ? JsonSerializer.Serialize(acquisition.Failure) : "Acquisition outcome requires reconciliation; no source permission or network access was granted to agents.", Version = updated.Version + 1 };
                if (await approvals.SaveAsync(blocked, updated.Version, ct)) updated = blocked;
            }
        }
        if (updated.Status == DependencyApprovalStatus.Approved)
            await executor.GrantAsync(new(id, updated.ProjectId, updated.RunId, project.RepositoryConnection, updated.Resolution!.PlanSha256, packageIds, owner, updated.Audit.Last().At), ct);
        return updated;
    }
    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in element.EnumerateObject()) { if (!names.Add(field.Name)) throw new ArgumentException("Duplicate dependency contract property."); RejectDuplicateProperties(field.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
    public static string ResolutionFingerprint(DependencyResolution resolution) => Hash(JsonSerializer.Serialize(resolution with { PlanSha256 = "" }));
    public static void ValidateResolution(DependencyApprovalRequest request, RepositoryConnection repository, DependencyResolution resolution)
    {
        if (resolution.PlanSha256 != ResolutionFingerprint(resolution) || resolution.Id != request.Id || resolution.ProjectId != request.ProjectId || resolution.RunId != request.RunId || resolution.Repository != repository || resolution.Packages.Length is < 1 or > 500 || resolution.Manifest.Length > 65536 || resolution.Lock.Length > 2_000_000 || !Regex.IsMatch(resolution.PlanSha256, "\\A[a-f0-9]{64}\\z") || resolution.Packages.Any(p => !PackageName().IsMatch(p.Name) || !ExactVersion().IsMatch(p.Version) || p.Source != "https://registry.npmjs.org" || p.Direct && p.CacheRequired == false || !Regex.IsMatch(p.Integrity, "\\Asha512-[A-Za-z0-9+/]{86}==\\z"))) throw new InvalidOperationException("Executor resolution does not match the authorized scope or immutable package integrity policy.");
        foreach (var direct in request.Proposal.Packages)
            if (!resolution.Packages.Any(x => x.Direct && x.Name == direct.Name && x.Version == direct.Version && x.Development == direct.Development)) throw new InvalidOperationException("Resolved plan omitted or changed a requested direct dependency.");
    }
    public async Task ResumeAsync(string id, CancellationToken ct, string? owner = null)
    {
        var request = await approvals.GetAsync(id, ct) ?? throw new KeyNotFoundException();
        if (request.Status is not (DependencyApprovalStatus.Approved or DependencyApprovalStatus.Rejected or DependencyApprovalStatus.ChangesRequested)) throw new InvalidOperationException("An owner decision is required before resume.");
        var run = await store.GetRunAsync(request.RunId, ct) ?? throw new KeyNotFoundException();
        if (run.CurrentStage != RunStage.AwaitingDependencyApproval || run.ResumeStage != request.Stage || run.TaskIndex != request.TaskIndex || run.ProjectId != request.ProjectId) throw new InvalidOperationException("Execution checkpoint no longer matches.");
        var pending = await approvals.ListAsync(request.ProjectId, request.RunId, ct);
        if (pending.Any(x => x.Stage == request.Stage && x.TaskIndex == request.TaskIndex && x.Status is DependencyApprovalStatus.Pending or DependencyApprovalStatus.Resolving or DependencyApprovalStatus.ReadyForReview)) throw new InvalidOperationException("Other dependencies still await owner action.");
        if (DateTimeOffset.UtcNow >= run.StartedAt.AddMinutes(run.Policy.MaxDurationMinutes) || run.Cost >= run.Policy.MaxBudgetUsd || run.ToolCalls >= run.Policy.MaxToolCalls || run.Iteration > run.Policy.MaxIterations) throw new ExecutionLimitException("Original deadline/budget/tool/iteration capacity is exhausted. Explicit separate owner limit authorization is required.");
        if (request.Status == DependencyApprovalStatus.Approved)
        {
            var project = await store.GetProjectAsync(run.ProjectId, ct) ?? throw new KeyNotFoundException();
            await executor.GrantAsync(new(request.Id, request.ProjectId, request.RunId, project.RepositoryConnection!, request.Resolution!.PlanSha256, request.Resolution.Packages.Select(p => p.Name + "@" + p.Version).Distinct().ToArray(), request.Audit.Last(x => x.Action == "Approve").Owner, request.Audit.Last(x => x.Action == "Approve").At), ct);
        }
        var resumeAudit = request with { Version = request.Version + 1, Audit = [.. request.Audit, new("Resume", owner ?? request.Audit.LastOrDefault()?.Owner ?? "Owner", DateTimeOffset.UtcNow, [], false, "Explicitly resume this checkpoint under the unchanged authorized policy")] };
        if (resumeAudit.Audit.Length > 100 || !await approvals.SaveAsync(resumeAudit, request.Version, ct)) throw new ConcurrencyException();
        var resumed = run.Transition(run.ResumeStage) with { FailureCategory = null, FailureReason = null, Outcome = null };
        if (!await store.SaveRunAsync(resumed, run.Version, null, ct)) throw new ConcurrencyException();
        await EventAsync(resumeAudit, "DependencyExecutionResuming", ct);
    }
    [GeneratedRegex(@"\A(?:@[a-z0-9][a-z0-9._-]{0,99}/)?[a-z0-9][a-z0-9._-]{0,99}\z")] public static partial Regex PackageName();
    [GeneratedRegex(@"\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-[a-zA-Z0-9.-]+)?\z")] public static partial Regex ExactVersion();
}
