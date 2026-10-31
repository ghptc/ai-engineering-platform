using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace AIEngineeringPlatform.Orchestration;

public sealed class EngineeringWorkflow(IPlatformStore store, PlatformService service, ContextBuilder contextBuilder, IEnumerable<IEngineeringAgent> agents, IEnumerable<IAgentTool> tools, IExecutionSandbox sandbox, InspectionProgressOptions? progressOptions = null)
{
    private readonly InspectionProgressOptions progress = progressOptions ?? new();
    private static string Key(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public async Task ProcessAsync(ExecutionRun run, string owner, CancellationToken ct)
    {
        await RefreshUsageAsync();
        run.CheckLimits(DateTimeOffset.UtcNow);
        var project = run.ProjectSnapshot ?? await service.ProjectAsync(run.ProjectId, ct);
        if (run.CurrentStage == RunStage.Pending && run.CurrentTask is { } task)
        {
            await CheckOwnershipAsync();
            var requirementId = Key($"{run.Id}/task/{run.TaskIndex}/requirement");
            await UpsertRecordAsync(new() { Id = requirementId, Kind = "requirement", ProjectId = project.Id, RunId = run.Id, Name = task.Title, Content = JsonSerializer.Serialize(new RequirementProposal(task.Title, task.Description, task.Title, task.AcceptanceCriteria, 1, [], false)), Status = "Pending", Metadata = new() { ["taskInput"] = "true", ["taskIndex"] = run.TaskIndex.ToString() } });
            run = run with { RequirementIds = run.RequirementIds.Append(requirementId).Distinct().ToArray() };
        }
        var inspections = new Dictionary<string, (string Id, ToolResult Result)>();
        var context = await contextBuilder.BuildAsync(project, run, ct);
        using (var inventory = JsonDocument.Parse(context))
        {
            await service.EventAsync(run, "ContextInventory", JsonSerializer.Serialize(inventory.RootElement.EnumerateObject().Select(x => new { Component = x.Name, Characters = x.Value.GetRawText().Length, TokensUpperBound = ContextBudget.Tokens(x.Value.GetRawText()) })), ct);
            await service.EventAsync(run, "ContextSourceInventory", JsonSerializer.Serialize(new { Sources = inventory.RootElement.GetProperty("SourceInventory"), Largest = inventory.RootElement.GetProperty("LargestArtifacts") }), ct);
        }
        var next = run.CurrentStage;
        var advanceTask = false;
        switch (run.CurrentStage)
        {
            case RunStage.Pending:
                await service.EventAsync(run, "RunStarted", "Execution started.", ct);
                await service.EventAsync(run, "AgentsSelected", JsonSerializer.Serialize(new { run.TaskIndex, Task = run.CurrentTask?.Title, Agents = run.SelectedAgents.Select(x => x.ToString()) }), ct);
                next = run.CurrentTask is { RequiresBusinessAnalysis: false } focused ? focused.RequiresArchitecture ? RunStage.Planning : RunStage.Implementing : RunStage.Analyzing; break;
            case RunStage.Analyzing:
                var analysis = await AgentAsync(AgentRole.BusinessAnalyst, ArtifactType.Requirements);
                var requirementIds = run.RequirementIds.ToList();
                foreach (var proposal in analysis.Requirements ?? [])
                {
                    if (string.IsNullOrWhiteSpace(proposal.Title) || proposal.AcceptanceCriteria.Length == 0 || proposal.Priority is < 1 or > 5) throw new InvalidOperationException("Business backlog contains an invalid requirement.");
                    var requirementId = Key(project.Id + "/requirement/" + proposal.Title.Trim().ToUpperInvariant());
                    await UpsertRecordAsync(new() { Id = requirementId, Kind = "requirement", ProjectId = project.Id, RunId = run.Id, Name = proposal.Title, Content = JsonSerializer.Serialize(proposal), Status = "Pending" });
                    var existingRequirement = await store.GetRecordAsync("requirement", requirementId, ct);
                    if (existingRequirement?.Status != "Verified") requirementIds.Add(requirementId);
                }
                run = run with { RequirementIds = requirementIds.Distinct().ToArray() };
                if (run.RequirementIds.Length == 0) throw new InvalidOperationException("No unfinished requirement was selected. Provide a new bounded task.");
                next = run.CurrentTask is { RequiresArchitecture: false } ? RunStage.PreparingContext : RunStage.Planning; break;
            case RunStage.Planning:
                await AgentAsync(AgentRole.Architect, ArtifactType.ArchitectureDecision); next = RunStage.PreparingContext; break;
            case RunStage.PreparingContext:
                await ArtifactAsync("context", ArtifactType.DevelopmentContext, JsonSerializer.Serialize(new { project.Id, run.RequirementIds, Source = "Live project, requirements, human instructions and architecture records; assembled on demand." })); next = RunStage.Implementing; break;
            case RunStage.Implementing:
                // Deferred planning blockers are durable, and cannot be cleared by a developer suggestion.
                var planningTasks = new List<RecordEntry>();
                for (var offset = 0; ; offset += 1000)
                {
                    var page = await store.RecordsAsync("task", run.ProjectId, run.Id, null, 0, offset, 1000, ct);
                    planningTasks.AddRange(page);
                    if (page.Count < 1000) break;
                }
                var blockers = planningTasks.Where(x => x.Metadata.GetValueOrDefault("taskIndex", "0") == run.TaskIndex.ToString() && x.Name is "BusinessAnalyst" or "Architect")
                    .Select(x => JsonSerializer.Deserialize<ModelExecutionResult>(x.Content)!.Output)
                    .Where(x => x.ToolCalls.Length == 0).SelectMany(x => x.Planning?.Blockers ?? [])
                    .Where(x => x.Stage == "Implementation").ToArray();
                if (blockers.Length > 0) throw new ConfigurationException("Planning identified execution prerequisites requiring owner reconciliation: " + string.Join("; ", blockers.Select(x => x.Reason)));
                if (!sandbox.Configured || !await sandbox.IsReadyAsync(ct)) throw new ConfigurationException("Secure executor is not ready. Verify authentication and Docker isolation, and rebuild/restart the host daemon with executor contract version 2 before resuming; no implementation operation was sent.");
                var correctionTargets = run.ReworkPlan.Where(x => x.Iteration == run.Iteration).Select(x => x.TargetAgent).ToArray();
                var inspectionRole = correctionTargets.FirstOrDefault(run.SelectedAgents.First(x => x is AgentRole.BackendEngineer or AgentRole.FrontendEngineer));
                var repositoryFacts = await ToolAsync("ReadRepository", "repository-facts", JsonSerializer.SerializeToElement(new { }), inspectionRole);
                if (!repositoryFacts.Success) throw new InvalidOperationException("Repository inspection failed.");
                using (var capabilityFacts = JsonDocument.Parse(repositoryFacts.Output))
                    if (!capabilityFacts.RootElement.TryGetProperty("TestProfiles", out var applicableTests) || applicableTests.GetArrayLength() == 0)
                        await service.EventAsync(run, "VerificationPrerequisite", "No applicable tests yet. Engineers may implement with authorized tools/build profiles; Test Engineer must create real tests. Owner approval of fixed test profiles is required before verification. No test or QA acceptance is granted.", ct);
                await ToolAsync("ReconcileRepository", "git-reconciliation", JsonSerializer.SerializeToElement(new { }), inspectionRole);
                var before = await ToolAsync("InspectDiff", "pre-implementation", JsonSerializer.SerializeToElement(new { }), inspectionRole);
                foreach (var developer in correctionTargets.Length > 0 ? correctionTargets : run.SelectedAgents.Where(x => x is AgentRole.BackendEngineer or AgentRole.FrontendEngineer))
                {
                    await AgentAsync(developer, ArtifactType.CodeDiff);
                    await ToolAsync("InspectDiff", "handoff-diff-" + developer, JsonSerializer.SerializeToElement(new { }), developer);
                }
                var after = await ToolAsync("InspectDiff", "post-implementation", JsonSerializer.SerializeToElement(new { }), inspectionRole);
                if (!before.Success || !after.Success) throw new InvalidOperationException("Cannot establish actual repository changes.");
                var implementedFacts = await ToolAsync("ReadRepository", "implemented-repository-facts", JsonSerializer.SerializeToElement(new { }), inspectionRole);
                using (var implementedJson = JsonDocument.Parse(implementedFacts.Output))
                    run = run with { NoSourceChanges = implementedFacts.Success && implementedJson.RootElement.TryGetProperty("HasSourceChanges", out var changed) ? !changed.GetBoolean() : before.WorkspaceRevision == after.WorkspaceRevision };
                await service.EventAsync(run, "TaskReconciled", JsonSerializer.Serialize(new { State = run.NoSourceChanges ? "CompletedButUnverified" : string.IsNullOrWhiteSpace(before.Output) ? "WorkNotStarted" : "PartiallyCompleted", before.WorkspaceRevision, CurrentRevision = after.WorkspaceRevision }), ct);
                next = run.NoSourceChanges ? RunStage.RunningTests : RunStage.GeneratingTests; break;
            case RunStage.GeneratingTests:
                await AgentAsync(AgentRole.TestEngineer, ArtifactType.TestReport); next = RunStage.RunningTests; break;
            case RunStage.RunningTests:
            {
                var testFacts = await ToolAsync("ReadRepository", "test-profile-facts", JsonSerializer.SerializeToElement(new { }), AgentRole.TestEngineer);
                using var testFactsJson = JsonDocument.Parse(testFacts.Output);
                var profiles = testFactsJson.RootElement.GetProperty("TestProfiles").EnumerateArray().Select(x => x.GetString()!).ToArray();
                var selectedProfile = profiles.Contains("all") ? "all" : profiles.Length == 1 ? profiles[0] : throw new ConfigurationException("Test verification blocked: owner must approve one applicable fixed test profile or an aggregate profile targeting the real test projects. Test Engineer must create missing test infrastructure first. Preserve the workspace and resume RunningTests after profile approval; no QA or delivery acceptance is granted.");
                var tests = await ToolAsync("RunTests", "required-tests", JsonSerializer.SerializeToElement(new { profile = selectedProfile }), AgentRole.TestEngineer);
                await ArtifactAsync("test-report", ArtifactType.TestReport, JsonSerializer.Serialize(tests));
                await service.EventAsync(run, "TestExecutionCompleted", JsonSerializer.Serialize(tests), ct);
                var testsPassed = tests.Success && tests.EvidenceId != null && tests.WorkspaceRevision != null;
                if (!testsPassed) run = run with { ReworkPlan = [.. run.ReworkPlan, .. ReworkPlanner.Create(run, AgentRole.TestEngineer, new AgentResult("Required tests failed; inspect persisted test evidence before correction.", "REQUEST_CHANGES", [], tests.EvidenceId == null ? [] : [tests.EvidenceId], [], [], "Repeat tests after correction", ReworkTargets: run.SelectedAgents.Where(x => x is AgentRole.BackendEngineer or AgentRole.FrontendEngineer).Select(x => x.ToString()).ToArray()), tests.WorkspaceRevision)] };
                next = testsPassed ? RunStage.Validating : RunStage.Implementing; break;
            }
            case RunStage.Validating:
                var testEvidence = await RequiredTestsAsync();
                context += "\nRequired test evidence: " + JsonSerializer.Serialize(new { testEvidence.Success, testEvidence.EvidenceId, testEvidence.WorkspaceRevision }) + "\nSelected requirement IDs: " + JsonSerializer.Serialize(run.RequirementIds);
                var qa = await AgentAsync(AgentRole.QAEngineer, ArtifactType.ValidationReport);
                var qaPassed = QualityGate.Approves(qa, testEvidence, run.RequirementIds);
                if (qaPassed && run.NoSourceChanges) await service.EventAsync(run, "TaskReconciled", JsonSerializer.Serialize(new { State = "CompletedAndVerified", testEvidence.EvidenceId, testEvidence.WorkspaceRevision }), ct);
                if (qaPassed) RecordTransition(AgentRole.QAEngineer, AgentRole.CodeReviewer);
                if (!qaPassed) run = run with { ReworkPlan = [.. run.ReworkPlan, .. ReworkPlanner.Create(run, AgentRole.QAEngineer, qa, testEvidence.WorkspaceRevision)] };
                next = qaPassed ? RunStage.Reviewing : RunStage.Implementing; break;
            case RunStage.Reviewing:
                if (!run.Policy.ReviewBeforeCommit && !run.NoSourceChanges)
                {
                    await RequestApprovalAsync("Create a commit and draft pull request after successful QA, then inspect and publish the review against that PR. This does not authorize merging.");
                    next = RunStage.AwaitingHumanApproval; break;
                }
                var diff = await ToolAsync("InspectDiff", "review-diff", JsonSerializer.SerializeToElement(new { }), AgentRole.CodeReviewer);
                if (!diff.Success || (!run.NoSourceChanges && string.IsNullOrWhiteSpace(diff.Output)) || diff.WorkspaceRevision != (await RequiredTestsAsync()).WorkspaceRevision) throw new InvalidOperationException("No actual diff is available for review.");
                await ArtifactAsync("actual-diff", ArtifactType.CodeDiff, diff.Output);
                var review = await AgentAsync(AgentRole.CodeReviewer, ArtifactType.CodeReview);
                await UpsertRecordAsync(new() { Id = Key(run.Id + "/review/" + run.Iteration), Kind = "review", RunId = run.Id, ProjectId = run.ProjectId, Name = "Local code review", Content = JsonSerializer.Serialize(review), Status = review.Decision });
                await service.EventAsync(run, "ReviewCompleted", review.Summary, ct);
                if (review.Decision == "APPROVE" && run.NoSourceChanges)
                {
                    var finalFacts = await ToolAsync("ReadRepository", "satisfied-revision", JsonSerializer.SerializeToElement(new { }), AgentRole.CodeReviewer);
                    var evidence = await RequiredTestsAsync();
                    if (!finalFacts.Success || finalFacts.WorkspaceRevision != evidence.WorkspaceRevision) throw new InvalidOperationException("Satisfied-task evidence revision mismatch.");
                    using var factsJson = JsonDocument.Parse(finalFacts.Output);
                    var selected = factsJson.RootElement.TryGetProperty("SelectedBranch", out var branchValue) && branchValue.ValueKind == JsonValueKind.String ? branchValue.GetString() : null;
                    await ArtifactAsync("existing-commit", ArtifactType.Commit, JsonSerializer.Serialize(new { sha = factsJson.RootElement.GetProperty("HeadCommit").GetString(), existing = true, evidence.WorkspaceRevision }));
                    var reconciled = await ToolAsync("ReconcileRepository", "satisfied-delivery", JsonSerializer.SerializeToElement(new { }), AgentRole.CodeReviewer);
                    if (!reconciled.Success) throw new ConfigurationException("Cannot reconcile existing delivery.");
                    using var prs = JsonDocument.Parse(reconciled.Output);
                    var head = factsJson.RootElement.GetProperty("HeadCommit").GetString();
                    if (selected != null)
                    {
                        var matching = prs.RootElement.GetProperty("pullRequests").EnumerateArray().Where(x => x.GetProperty("branch").GetString() == selected && x.GetProperty("commit").GetString() == head && (x.GetProperty("state").GetString() == "open" || x.GetProperty("merged").ValueKind != JsonValueKind.Null)).ToArray();
                        if (!reconciled.Success || matching.Length != 1) throw new ConfigurationException("Existing feature work is verified but has no matching active or merged PR; delivery requires human reconciliation.");
                        await ArtifactAsync("existing-pull-request", ArtifactType.PullRequest, matching[0].GetRawText());
                        await service.EventAsync(run, "TaskReconciled", JsonSerializer.Serialize(new { State = "AlreadyDelivered", PullRequest = matching[0], evidence.EvidenceId, evidence.WorkspaceRevision }), ct);
                    }
                    else
                    {
                        foreach (var existingPr in prs.RootElement.GetProperty("pullRequests").EnumerateArray().Where(x => x.GetProperty("merged").ValueKind != JsonValueKind.Null && (x.GetProperty("commit").GetString() == head || x.TryGetProperty("mergeCommit", out var mergedCommit) && mergedCommit.GetString() == head)))
                        {
                            await ArtifactAsync("existing-pull-request-" + existingPr.GetProperty("number").GetInt32(), ArtifactType.PullRequest, existingPr.GetRawText());
                            await service.EventAsync(run, "TaskReconciled", JsonSerializer.Serialize(new { State = "AlreadyDelivered", PullRequest = existingPr, evidence.EvidenceId, evidence.WorkspaceRevision }), ct);
                        }
                    }
                    await store.MarkRequirementsVerifiedAsync(run.RequirementIds, run.Id, evidence.EvidenceId!, ct);
                    run = run with { Outcome = ExecutionOutcome.AlreadySatisfied };
                    await service.EventAsync(run, "AlreadySatisfied", JsonSerializer.Serialize(new { evidence.EvidenceId, evidence.WorkspaceRevision, Repository = factsJson.RootElement, Delivery = selected == null ? "Verified on default-branch source" : "Existing PR; no duplicate delivery" }), ct);
                    advanceTask = run.TaskIndex + 1 < run.Tasks.Length;
                    next = RunStage.Completed; break;
                }
                if (review.Decision != "APPROVE") run = run with { ReworkPlan = [.. run.ReworkPlan, .. ReworkPlanner.Create(run, AgentRole.CodeReviewer, review, diff.WorkspaceRevision)] };
                next = review.Decision == "APPROVE" ? RunStage.AwaitingHumanApproval : RunStage.Implementing;
                if (next == RunStage.AwaitingHumanApproval)
                {
                    await RequestApprovalAsync("Approve preparing a commit and draft pull request from the reviewed workspace. This does not authorize merging.");
                }
                break;
            case RunStage.PreparingDelivery:
                var verifiedTests = await RequiredTestsAsync();
                var deliveryFacts = await ToolAsync("ReadRepository", "delivery-repository-facts", JsonSerializer.SerializeToElement(new { }), AgentRole.CodeReviewer);
                using (var deliveryJson = JsonDocument.Parse(deliveryFacts.Output))
                    if (deliveryJson.RootElement.TryGetProperty("SelectedBranch", out var existingBranch) && existingBranch.ValueKind == JsonValueKind.String)
                        throw new TaskReconciliationException(ExecutionOutcome.AwaitingHumanInput, "Reconciled feature work needs further changes. An explicit existing-branch update is required; duplicate PR delivery is blocked.");
                var deliveryDiff = await ToolAsync("InspectDiff", "delivery-diff", JsonSerializer.SerializeToElement(new { }), AgentRole.CodeReviewer);
                if (!deliveryDiff.Success || deliveryDiff.WorkspaceRevision != verifiedTests.WorkspaceRevision) throw new InvalidOperationException("Workspace changed after tests; rerun validation before delivery.");
                var commit = await ToolAsync("CreateCommit", "commit", JsonSerializer.SerializeToElement(new { branch = "engineering/" + run.Id, message = "feat: " + (run.CurrentTask?.Title ?? "implement reviewed engineering task"), expectedReview = run.Policy.ReviewBeforeCommit ? Key(run.Id + "/review/" + run.Iteration) : null, expectedRevision = verifiedTests.WorkspaceRevision }), AgentRole.CodeReviewer, true);
                if (!commit.Success) throw new InvalidOperationException("Commit creation failed: " + commit.Output);
                await ArtifactAsync("commit", ArtifactType.Commit, commit.Output);
                var push = await ToolAsync("PushBranch", "push", JsonSerializer.SerializeToElement(new { branch = "engineering/" + run.Id }), AgentRole.CodeReviewer, true);
                if (!push.Success) throw new InvalidOperationException("Push failed: " + push.Output);
                var pr = await ToolAsync("CreatePullRequest", "pull-request", JsonSerializer.SerializeToElement(new { branch = "engineering/" + run.Id, title = "Engineering task: " + project.Name, body = "Validated local changes. See platform run " + run.Id + ". Local review is recorded separately; no GitHub review has been published." }), AgentRole.CodeReviewer, true);
                if (!pr.Success) throw new InvalidOperationException("Pull request creation failed.");
                await ArtifactAsync("pull-request", ArtifactType.PullRequest, pr.Output);
                using (var prJson = JsonDocument.Parse(pr.Output))
                {
                    var number = prJson.RootElement.GetProperty("number").GetInt32();
                    if (number <= 0) throw new InvalidOperationException("GitHub returned no real PR number.");
                    var publishedDecision = "COMMENT";
                    AgentResult? deliveryReview = null;
                    var localReview = await store.GetRecordAsync("review", Key(run.Id + "/review/" + run.Iteration), ct);
                    var reviewBody = "## Platform local review\n\n" + (localReview == null ? "Local review approved before delivery." : ReviewMarkdown.Format(JsonSerializer.Deserialize<AgentResult>(localReview.Content)!)) + "\n\nTest evidence: " + verifiedTests.EvidenceId + "\nTested source revision: " + verifiedTests.WorkspaceRevision + "\n\nThis records the platform's actual local review; it is not independent human approval.";
                    if (!run.Policy.ReviewBeforeCommit)
                    {
                        var prEvidence = await ToolAsync("ReadPullRequest", "pr-evidence", JsonSerializer.SerializeToElement(new { number }), AgentRole.CodeReviewer, true);
                        if (!prEvidence.Success) throw new InvalidOperationException("Cannot review PR without actual changes.");
                        var publishedReview = await AgentAsync(AgentRole.CodeReviewer, ArtifactType.CodeReview);
                        deliveryReview = publishedReview;
                        publishedDecision = publishedReview.Decision switch { "APPROVE" => "APPROVE", "REQUEST_CHANGES" => "REQUEST_CHANGES", _ => "COMMENT" };
                        reviewBody = ReviewMarkdown.Format(publishedReview);
                        await UpsertRecordAsync(new() { Id = Key(run.Id + "/pr-review/" + run.Iteration), Kind = "review", RunId = run.Id, ProjectId = run.ProjectId, Name = "Pull request review", Content = JsonSerializer.Serialize(publishedReview), Status = publishedDecision });
                    }
                    var published = await ToolAsync("SubmitCodeReview", "publish-review", JsonSerializer.SerializeToElement(new { number, decision = publishedDecision, body = reviewBody }), AgentRole.CodeReviewer, true);
                    if (!published.Success) throw new InvalidOperationException("GitHub did not confirm the published review.");
                    await ArtifactAsync("published-review", ArtifactType.CodeReview, published.Output);
                    await service.EventAsync(run, "ReviewCompleted", "GitHub confirmed PR review submission.", ct);
                    if (publishedDecision != "APPROVE" && !run.Policy.ReviewBeforeCommit) { run = run with { ReworkPlan = [.. run.ReworkPlan, .. ReworkPlanner.Create(run, AgentRole.CodeReviewer, deliveryReview!, verifiedTests.WorkspaceRevision)] }; next = RunStage.Implementing; break; }
                }
                await store.MarkRequirementsVerifiedAsync(run.RequirementIds, run.Id, verifiedTests.EvidenceId!, ct);
                await ArtifactAsync("next-context", ArtifactType.NextContext, JsonSerializer.Serialize(new { run.RequirementIds, run.TaskIndex, verifiedTests.EvidenceId, verifiedTests.WorkspaceRevision, Retrieval = "ReadProjectMemory retrieves original artifacts; task context is rebuilt on demand." }));
                if (run.TaskIndex + 1 < run.Tasks.Length)
                {
                    await ArtifactAsync("next-task", ArtifactType.NextContext, JsonSerializer.Serialize(new { Completed = run.CurrentTask?.Title, NextAction = run.Tasks[run.TaskIndex + 1], BudgetRemainingUsd = run.Policy.MaxBudgetUsd - run.Cost }));
                    await service.EventAsync(run, "TaskCompleted", JsonSerializer.Serialize(new { run.TaskIndex, run.RequirementIds, verifiedTests.EvidenceId, verifiedTests.WorkspaceRevision }), ct);
                    advanceTask = true;
                }
                run = run with { Outcome = ExecutionOutcome.Implemented };
                next = RunStage.Completed; break;
        }
        var updated = advanceTask ? run.AdvanceTask() : run.Transition(next);
        if (!await store.SaveRunAsync(updated, run.Version, owner, ct)) throw new ConcurrencyException();
        // State and terminal events are committed atomically with the run and delivered by the outbox.

        void RecordTransition(AgentRole source, AgentRole target)
        {
            if (!run.AgentTransitions.Any(x => x.SourceAgent == source && x.TargetAgent == target && x.TaskIndex == run.TaskIndex && x.Iteration == run.Iteration))
                run = run with { AgentTransitions = [.. run.AgentTransitions, new(source, target, run.TaskIndex, run.Iteration, DateTimeOffset.UtcNow)] };
        }
        string NextAgent(AgentRole role)
        {
            if (role is AgentRole.QAEngineer or AgentRole.CodeReviewer) return ""; // Quality gates decide, not model status.
            var corrections = run.ReworkPlan.Where(x => x.Iteration == run.Iteration).Select(x => x.TargetAgent).ToArray();
            var route = corrections.Length > 0 ? corrections.Concat(new[] { AgentRole.TestEngineer, AgentRole.QAEngineer, AgentRole.CodeReviewer }).ToArray() : run.SelectedAgents;
            return route.SkipWhile(x => x != role).Skip(1).Select(x => x.ToString()).FirstOrDefault() ?? "";
        }
        async Task RefreshUsageAsync()
        {
            async Task<RecordEntry[]> Records(string kind)
            {
                var all = new List<RecordEntry>();
                for (var offset = 0; ; offset += 1000)
                {
                    var page = await store.RecordsAsync(kind, run.ProjectId, run.Id, null, 0, offset, 1000, ct);
                    all.AddRange(page);
                    if (page.Count < 1000) return all.ToArray();
                }
            }
            var tasks = await Records("task");
            var invocations = await Records("tool");
            var providerCharges = await ProviderAccounting.ChargesAsync(store, run.Id, ct);
            var usage = tasks.Select(x => JsonSerializer.Deserialize<ModelExecutionResult>(x.Content)!).ToArray();
            var counters = tasks.Select(x => (Role: x.Name, Task: x.Metadata.GetValueOrDefault("taskIndex") ?? "0"))
                .Concat(invocations.Select(x => (Role: x.Metadata.GetValueOrDefault("agent") ?? "", Task: x.Metadata.GetValueOrDefault("taskIndex") ?? "0"))).Distinct()
                .Where(x => Enum.TryParse<AgentRole>(x.Role, out _)).Select(key =>
                {
                    var operations = invocations.Where(x => x.Metadata.GetValueOrDefault("agent") == key.Role && (x.Metadata.GetValueOrDefault("taskIndex") ?? "0") == key.Task).ToArray();
                    return new AgentUsage(Enum.Parse<AgentRole>(key.Role), int.Parse(key.Task), tasks.Count(x => x.Name == key.Role && (x.Metadata.GetValueOrDefault("taskIndex") ?? "0") == key.Task), operations.Length, operations.Count(x => x.Status == "Failed"), operations.Count(x => x.Metadata.GetValueOrDefault("retry") == "true"));
                }).ToArray();
            run = run with { Cost = usage.Where(x => x.Diagnostics == null).Sum(x => x.Cost) + providerCharges.Sum(x => x.Cost), TokenUsage = usage.Where(x => x.Diagnostics == null).Sum(x => x.InputTokens + x.OutputTokens) + providerCharges.Sum(x => (x.Diagnostics.InputTokens ?? 0) + (x.Diagnostics.OutputTokens ?? 0)), ToolCalls = invocations.Length, Usage = counters };
        }
        async Task<ToolResult> RequiredTestsAsync()
        {
            var id = Key($"{run.Id}/{RunStage.RunningTests}/{run.Iteration}/required-tests");
            var record = await store.GetRecordAsync("tool", id, ct) ?? throw new InvalidOperationException("Required test execution evidence is absent.");
            var result = JsonSerializer.Deserialize<ToolResult>(record.Content)!;
            if (!result.Success || string.IsNullOrWhiteSpace(result.EvidenceId) || string.IsNullOrWhiteSpace(result.WorkspaceRevision)) throw new InvalidOperationException("Tests did not pass for a verified workspace revision.");
            return result;
        }
        async Task RequestApprovalAsync(string description)
        {
            await UpsertRecordAsync(new() { Id = Key(run.Id + "/approval/" + run.Iteration), Kind = "approval", RunId = run.Id, ProjectId = run.ProjectId, Name = "Prepare delivery", Content = description, Status = "Pending" });
            await service.EventAsync(run, "ApprovalRequested", "Human delivery approval required.", ct);
        }
        async Task UpsertRecordAsync(RecordEntry record) { if (await store.GetRecordAsync(record.Kind, record.Id, ct) == null) await store.AppendAsync(record, ct); }
        async Task ArtifactAsync(string suffix, ArtifactType type, string content)
        {
            var id = Key($"{run.Id}/{run.CurrentStage}/{run.Iteration}/artifact/{suffix}");
            if (await store.GetRecordAsync("artifact", id, ct) != null) return;
            await store.AppendAsync(new() { Id = id, Kind = "artifact", ProjectId = project.Id, RunId = run.Id, Name = type.ToString(), Content = content, Metadata = new() { ["sha256"] = Key(content), ["taskIndex"] = run.TaskIndex.ToString(), ["stage"] = run.Status, ["format"] = suffix == "actual-diff" ? "diff" : "json" } }, ct);
            await service.EventAsync(run, "ArtifactCreated", JsonSerializer.Serialize(new { id, type = type.ToString() }), ct);
        }
        async Task CheckOwnershipAsync()
        {
            var current = await service.RunAsync(run.Id, ct);
            if (current.LeaseOwner != owner || current.CurrentStage != run.CurrentStage || current.Version != run.Version || current.LeaseExpiry < DateTimeOffset.UtcNow) throw new ConcurrencyException();
            run.CheckLimits(DateTimeOffset.UtcNow);
        }
        async Task<ToolResult> ToolAsync(string name, string suffix, JsonElement args, AgentRole role, bool delivery = false)
        {
            await CheckOwnershipAsync();
            var operationId = Key($"{run.Id}/{run.CurrentStage}/{run.Iteration}/{suffix}");
            var saved = await store.GetRecordAsync("tool", operationId, ct);
            if (saved != null)
            {
                var previous = JsonSerializer.Deserialize<ToolResult>(saved.Content)!;
                context += ContextBudget.ToolEvidence(operationId, name, previous);
                return previous;
            }
            var readOnlyInspection = name is "ReadRepository" or "ListFiles" or "ReadFile" or "SearchCode" or "InspectDiff";
            if (!readOnlyInspection)
            {
                for (var offset = 0; ; offset += 1000)
                {
                    var priorFailures = await store.RecordsAsync("event", project.Id, run.Id, null, 0, offset, 1000, ct, "Blocked");
                    var unresolved = priorFailures.FirstOrDefault(x => x.Name == "ExecutorOperationBlocked" && x.Metadata.GetValueOrDefault("operationId") == operationId && !string.Equals(x.Metadata.GetValueOrDefault("outcomeUnknown"), "False", StringComparison.OrdinalIgnoreCase));
                    if (unresolved != null)
                        throw new ExecutorFailureException(ExecutorFailureCategory.WorkspaceConflict, operationId, name, int.TryParse(unresolved.Metadata.GetValueOrDefault("httpStatus"), out var recordedStatus) ? recordedStatus : null, true, ExecutorFailureReason.ReconciliationRequired);
                    if (priorFailures.Count < 1000) break;
                    await CheckOwnershipAsync();
                }
            }
            var inspectionKey = name + "/" + Key(args.GetRawText());
            if (readOnlyInspection && inspections.TryGetValue(inspectionKey, out var cached))
            {
                context += ContextBudget.ToolEvidence(cached.Id, name, cached.Result);
                await service.EventAsync(run, "InspectionReused", JsonSerializer.Serialize(new { SourceId = cached.Id, name, cached.Result.WorkspaceRevision }), ct);
                return cached.Result;
            }
            // Remote reads and memory retrieval do not invalidate local source observations.
            if (!readOnlyInspection && name is not ("ReconcileRepository" or "ReadPullRequest" or "ReadProjectMemory" or "SaveArtifact")) inspections.Clear();
            var definition = (await store.AgentsAsync(ct)).Single(x => x.Role == role);
            if (delivery) definition = definition with { AllowedTools = [name] };
            await service.EventAsync(run, "ToolInvocationStarted", name, ct);
            var priorOperations = await store.RecordsAsync("tool", project.Id, run.Id, null, 0, 0, 1000, ct);
            var argumentsHash = Key(args.GetRawText());
            var isRetry = priorOperations.Any(x => x.Name == name && x.Status == "Failed" && x.Metadata.GetValueOrDefault("argumentsHash") == argumentsHash);
            var startedAt = DateTimeOffset.UtcNow;
            var toolStarted = System.Diagnostics.Stopwatch.StartNew();
            ToolResult result;
            try { result = await tools.Single(x => x.Name == name).ExecuteAsync(new(project, run, context, definition), args, operationId, ct); }
            catch (ExecutorFailureException failure)
            {
                await CheckOwnershipAsync();
                try
                {
                    await store.AppendAsync(new() { Id = $"executor-failure-{operationId}-{run.Version}", Kind = "event", ProjectId = project.Id, RunId = run.Id, Name = "ExecutorOperationBlocked", Content = failure.Message, Status = "Blocked", Metadata = new() { ["operationId"] = operationId, ["category"] = failure.Category.ToString(), ["httpStatus"] = failure.StatusCode?.ToString() ?? "", ["outcomeUnknown"] = failure.OutcomeUnknown.ToString(), ["reason"] = failure.Reason.ToString(), ["argumentsHash"] = argumentsHash, ["attemptVersion"] = run.Version.ToString() } }, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception persistence) { throw new ExecutorEvidenceException(failure, persistence); }
                throw;
            }
            catch (Exception exception) when (exception is ArgumentException or JsonException) { result = new(false, exception.Message); }
            await CheckOwnershipAsync();
            if (readOnlyInspection && result.Success && result.WorkspaceRevision != null) inspections[inspectionKey] = (operationId, result);
            await store.AppendAsync(new() { Id = operationId, Kind = "tool", ProjectId = project.Id, RunId = run.Id, Name = name, Content = JsonSerializer.Serialize(result), Status = result.Success ? "Succeeded" : "Failed", Metadata = new() { ["argumentsHash"] = argumentsHash, ["retry"] = isRetry ? "true" : "false", ["agent"] = role.ToString(), ["taskIndex"] = run.TaskIndex.ToString(), ["startedAt"] = startedAt.ToString("O"), ["completedAt"] = DateTimeOffset.UtcNow.ToString("O"), ["operation"] = name, ["profile"] = args.TryGetProperty("profile", out var profileValue) ? profileValue.GetString() ?? "" : "", ["durationMs"] = toolStarted.ElapsedMilliseconds.ToString(), ["leaseOwner"] = owner, ["fence"] = run.Version.ToString(), ["iteration"] = run.Iteration.ToString(), ["stage"] = run.Status } }, ct);
            await RefreshUsageAsync();
            if (!await store.SaveRunAsync(run, run.Version, owner, ct)) throw new ConcurrencyException();
            run = await service.RunAsync(run.Id, ct);
            if (ContextBudget.Tokens(result.Output) > 8000)
                await service.EventAsync(run, "ContextCompacted", JsonSerializer.Serialize(new { SourceKind = "tool", SourceId = operationId, BeforeTokens = ContextBudget.Tokens(result.Output), Strategy = "Bounded preview with persisted original and paged retrieval" }), ct);
            await service.EventAsync(run, "ToolInvocationCompleted", JsonSerializer.Serialize(new { name, result.Success, result.EvidenceId }), ct);
            context += ContextBudget.ToolEvidence(operationId, name, result);
            return result;
        }
        async Task<AgentResult> AgentAsync(AgentRole role, ArtifactType artifactType)
        {
            var definition = (await store.AgentsAsync(ct)).Single(x => x.Role == role);
            // Each specialist starts from persisted task knowledge, not another specialist's transcript.
            {
                context = await contextBuilder.BuildAsync(project, run, ct);
                if (role == AgentRole.QAEngineer)
                {
                    var required = await RequiredTestsAsync();
                    context += "\nRequired test evidence: " + JsonSerializer.Serialize(new { required.EvidenceId, required.WorkspaceRevision, RequirementIds = run.RequirementIds, Contract = "Return this exact bare EvidenceId as an item in evidence and in each passing check's evidenceId. Put explanations in details, not in the evidence ID array. Companion diff evidence may support scope checks, but required tests must remain the check evidenceId." });
                }
            }
            var conversationId = Key(run.Id + "/collaboration");
            await UpsertRecordAsync(new() { Id = conversationId, Kind = "conversation", ProjectId = project.Id, RunId = run.Id, Name = "AgentConversation" });
            for (var turn = 0; turn < 1000; turn++)
            {
                await CheckOwnershipAsync();
                var agentCount = run.Usage.Where(x => x.Agent == role).Sum(x => x.ToolInvocations);
                var agentBudget = run.Policy.AgentToolBudgets?.GetValueOrDefault(role, run.Policy.MaxToolCalls) ?? run.Policy.MaxToolCalls;
                if (run.ToolCalls >= run.Policy.MaxToolCalls * 0.8 || agentCount >= agentBudget * 0.8)
                    context += "\nCapacity notice: " + JsonSerializer.Serialize(new { RemainingExecutionTools = run.Policy.MaxToolCalls - run.ToolCalls, AgentAdvisoryRemaining = agentBudget - agentCount, Instruction = "Checkpoint completed work and evidence now. Reuse revision evidence. Do not start a multi-operation change unless it fits remaining capacity. Agent allocation is advisory; unused execution capacity remains shared." });
                var taskId = Key($"{run.Id}/{run.CurrentStage}/{run.Iteration}/{role}/{turn}");
                var saved = await store.GetRecordAsync("task", taskId, ct);
                if (saved == null)
                {
                    if (run.CurrentStage is RunStage.Implementing or RunStage.GeneratingTests)
                    {
                        if (!progress.IsValid) throw new ConfigurationException("Invalid inspection progress thresholds; feedback must precede a stop within 1000 repeated turns.");
                        var progressRecords = new List<RecordEntry>();
                        foreach (var kind in new[] { "task", "tool" })
                            for (var offset = 0; ; offset += 1000)
                            {
                                var page = await store.RecordsAsync(kind, project.Id, run.Id, null, 0, offset, 1000, ct);
                                progressRecords.AddRange(page.Where(x => x.Metadata.GetValueOrDefault("taskIndex", "0") == run.TaskIndex.ToString()
                                    && x.Metadata.GetValueOrDefault("iteration") == run.Iteration.ToString()
                                    && (kind == "task" ? x.Name == role.ToString() : x.Metadata.GetValueOrDefault("agent") == role.ToString())));
                                if (page.Count < 1000) break;
                            }
                        var repeatedTurns = InspectionProgress.RepeatedTurns(progressRecords);
                        if (repeatedTurns >= progress.StopAfterRepeatedTurns)
                        {
                            await service.EventAsync(run, "InspectionProgressBlocked", JsonSerializer.Serialize(new { role, repeatedTurns, Instruction = InspectionProgress.Feedback }), ct);
                            throw new TaskReconciliationException(ExecutionOutcome.Blocked, InspectionProgress.Feedback + " Inspection limit reached; human reconciliation is required before further model work.");
                        }
                        if (repeatedTurns >= progress.FeedbackAfterRepeatedTurns)
                        {
                            if (!context.Contains("\nInspection progress feedback: " + InspectionProgress.Feedback, StringComparison.Ordinal))
                                context += "\nInspection progress feedback: " + InspectionProgress.Feedback;
                            await service.EventAsync(run, "InspectionProgressFeedback", JsonSerializer.Serialize(new { role, repeatedTurns }), ct);
                        }
                    }
                }
                var beforeTokens = ContextBudget.Tokens(context);
                try { context = ContextBudget.Compact(context, definition); }
                catch (InvalidOperationException)
                {
                    await service.EventAsync(run, "ContextBudgetExceeded", JsonSerializer.Serialize(new { role, definition.Model, ContextTokensUpperBound = beforeTokens, InstructionTokensUpperBound = ContextBudget.Tokens(definition.SystemInstructions), ProtocolAndFramingReserve = 13024, InputLimit = ContextBudget.InputLimit(definition), OutputReserved = definition.MaxOutputTokens, Components = ContextBudget.Inventory(context) }), ct);
                    throw;
                }
                await service.EventAsync(run, "ContextBudgetEvaluated", JsonSerializer.Serialize(new { role, definition.Model, BeforeTokens = beforeTokens, InputTokensUpperBound = ContextBudget.Tokens(context), InstructionTokensUpperBound = ContextBudget.Tokens(definition.SystemInstructions), ProtocolAndFramingReserve = 13024, InputLimit = ContextBudget.InputLimit(definition), OutputReserved = definition.MaxOutputTokens, Compacted = beforeTokens != ContextBudget.Tokens(context), Estimator = "utf8-byte-upper-bound" }), ct);
                ModelExecutionResult response;
                if (saved != null) response = JsonSerializer.Deserialize<ModelExecutionResult>(saved.Content)!;
                else
                {
                    await service.EventAsync(run, "AgentStarted", role.ToString(), ct);
                    var modelStarted = System.Diagnostics.Stopwatch.StartNew();
                    try { response = await agents.Single(x => x.Role == role).ExecuteAsync(new(project, run, context, definition), ct); }
                    catch (ProviderException exception)
                    {
                        await service.EventAsync(run, "ProviderRequestFailed", JsonSerializer.Serialize(exception.Diagnostics), ct);
                        await RefreshUsageAsync();
                        if (!await store.SaveRunAsync(run, run.Version, owner, ct)) throw new ConcurrencyException();
                        throw;
                    }
                    await CheckOwnershipAsync();
                    await store.AppendAsync(new() { Id = taskId, Kind = "task", ProjectId = project.Id, RunId = run.Id, Name = role.ToString(), Content = JsonSerializer.Serialize(response), Status = "Completed", Metadata = new() { ["durationMs"] = modelStarted.ElapsedMilliseconds.ToString(), ["responseId"] = response.ResponseId, ["taskIndex"] = run.TaskIndex.ToString(), ["iteration"] = run.Iteration.ToString(), ["model"] = definition.Model } }, ct);
                }
                // Task checkpoints are the durable usage ledger; recompute to avoid charging cached responses twice.
                await RefreshUsageAsync();
                if (!await store.SaveRunAsync(run, run.Version, owner, ct)) throw new ConcurrencyException();
                run = await service.RunAsync(run.Id, ct);
                var result = response.Output;
                if (run.CurrentStage is RunStage.Analyzing or RunStage.Planning && result.ToolCalls.Length == 0 && result.Planning?.Blockers.Any(x => x.Stage == "Planning") == true)
                    result = result with { Decision = "BLOCKED" };
                if (run.CurrentStage is RunStage.Analyzing or RunStage.Planning && PlanningPolicy.DefersExecutionBlockers(role, result))
                {
                    // Original model decision remains in task/message evidence; only routing is stage-aware.
                    await service.EventAsync(run, "ExecutionBlockersDeferred", JsonSerializer.Serialize(new { role, result.Planning }), ct);
                    result = result with { Decision = "CONTINUE" };
                }
                var handoffId = Key(taskId + "/handoff");
                await UpsertRecordAsync(new() { Id = handoffId, Kind = "handoff", ProjectId = project.Id, RunId = run.Id, Name = role.ToString(), Status = result.Decision is "BLOCKED" or "AWAITING_HUMAN_INPUT" ? "Blocked" : result.ToolCalls.Length == 0 ? "Completed" : "Working", Content = JsonSerializer.Serialize(new AgentHandoff(result.Summary, result.ToolCalls.Length == 0 ? result.Summary : "Work in progress; use persisted tool evidence.", result.Evidence, result.Checks ?? [], result.OpenQuestions ?? [], result.NextContext)), Metadata = new() { ["taskIndex"] = run.TaskIndex.ToString(), ["iteration"] = run.Iteration.ToString(), ["targetAgent"] = result.ToolCalls.Length == 0 ? NextAgent(role) : role.ToString(), ["sourceTaskId"] = taskId } });
                await service.EventAsync(run, "AgentHandoffCreated", JsonSerializer.Serialize(new { handoffId, role, run.TaskIndex }), ct);
                // Full structured output is persisted above; subsequent turns retrieve it on demand.
                var messageId = Key(taskId + "/message");
                if (await store.GetRecordAsync("message", messageId, ct) == null)
                {
                    await store.AppendAsync(new() { Id = messageId, Kind = "message", ProjectId = project.Id, RunId = run.Id, ConversationId = conversationId, Name = role.ToString(), Content = JsonSerializer.Serialize(response.Output), Metadata = new() { ["responseId"] = response.ResponseId } }, ct);
                    await service.EventAsync(run, "AgentMessageCreated", JsonSerializer.Serialize(new { messageId, role = role.ToString() }), ct);
                }
                if (result.Decision is "BLOCKED" or "AWAITING_HUMAN_INPUT") throw new TaskReconciliationException(result.Decision == "BLOCKED" ? ExecutionOutcome.Blocked : ExecutionOutcome.AwaitingHumanInput, result.Summary);
                if (result.ToolCalls.Length == 0) { await ArtifactAsync(role.ToString(), artifactType, JsonSerializer.Serialize(result)); if (Enum.TryParse<AgentRole>(NextAgent(role), out var target)) RecordTransition(role, target); return result; }
                var remainingBatch = 0;
                for (var i = 0; i < result.ToolCalls.Length; i++)
                    if (await store.GetRecordAsync("tool", Key($"{run.Id}/{run.CurrentStage}/{run.Iteration}/{taskId}/tool/{i}"), ct) == null) remainingBatch++;
                if (remainingBatch > run.Policy.MaxToolCalls - run.ToolCalls) throw new ExecutionLimitException("Requested tool batch cannot fit remaining capacity. Model checkpoint saved; approve a limit extension before resuming.");
                for (var i = 0; i < result.ToolCalls.Length; i++)
                {
                    var call = result.ToolCalls[i];
                    await ToolAsync(call.Name, taskId + "/tool/" + i, call.Arguments, role);
                }
            }
            throw new InvalidOperationException("Agent tool loop exceeded the configured limit.");
        }
    }
}
