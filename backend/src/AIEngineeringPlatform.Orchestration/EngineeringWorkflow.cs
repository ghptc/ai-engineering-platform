using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace AIEngineeringPlatform.Orchestration;

public sealed class EngineeringWorkflow(IPlatformStore store, PlatformService service, ContextBuilder contextBuilder, IEnumerable<IEngineeringAgent> agents, IEnumerable<IAgentTool> tools, IExecutionSandbox sandbox)
{
    private static string Key(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public async Task ProcessAsync(ExecutionRun run, string owner, CancellationToken ct)
    {
        run.CheckLimits(DateTimeOffset.UtcNow);
        var project = run.ProjectSnapshot ?? await service.ProjectAsync(run.ProjectId, ct);
        if (run.CurrentStage == RunStage.Pending && run.CurrentTask is { } task)
        {
            await CheckOwnershipAsync();
            var requirementId = Key($"{run.Id}/task/{run.TaskIndex}/requirement");
            await UpsertRecordAsync(new() { Id = requirementId, Kind = "requirement", ProjectId = project.Id, RunId = run.Id, Name = task.Title, Content = JsonSerializer.Serialize(new RequirementProposal(task.Title, task.Description, task.Title, task.AcceptanceCriteria, 1, [], false)), Status = "Pending", Metadata = new() { ["taskInput"] = "true", ["taskIndex"] = run.TaskIndex.ToString() } });
            run = run with { RequirementIds = run.RequirementIds.Append(requirementId).Distinct().ToArray() };
        }
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
                if (!sandbox.Configured) throw new ConfigurationException("Configure and start the secure execution sandbox before implementing source changes.");
                var before = await ToolAsync("InspectDiff", "pre-implementation", JsonSerializer.SerializeToElement(new { }), AgentRole.BackendEngineer);
                foreach (var developer in run.SelectedAgents.Where(x => x is AgentRole.BackendEngineer or AgentRole.FrontendEngineer))
                    await AgentAsync(developer, ArtifactType.CodeDiff);
                var after = await ToolAsync("InspectDiff", "post-implementation", JsonSerializer.SerializeToElement(new { }), AgentRole.BackendEngineer);
                if (!after.Success || string.IsNullOrWhiteSpace(after.Output) || before.Output == after.Output) throw new InvalidOperationException("Implementation produced no verified repository changes.");
                next = RunStage.GeneratingTests; break;
            case RunStage.GeneratingTests:
                await AgentAsync(AgentRole.TestEngineer, ArtifactType.TestReport); next = RunStage.RunningTests; break;
            case RunStage.RunningTests:
                var tests = await ToolAsync("RunTests", "required-tests", JsonSerializer.SerializeToElement(new { profile = "all" }), AgentRole.TestEngineer);
                await ArtifactAsync("test-report", ArtifactType.TestReport, JsonSerializer.Serialize(tests));
                await service.EventAsync(run, "TestExecutionCompleted", JsonSerializer.Serialize(tests), ct);
                next = tests.Success && tests.EvidenceId != null && tests.WorkspaceRevision != null ? RunStage.Validating : RunStage.Implementing; break;
            case RunStage.Validating:
                var testEvidence = await RequiredTestsAsync();
                context += "\nRequired test evidence: " + JsonSerializer.Serialize(new { testEvidence.Success, testEvidence.EvidenceId, testEvidence.WorkspaceRevision }) + "\nSelected requirement IDs: " + JsonSerializer.Serialize(run.RequirementIds);
                var qa = await AgentAsync(AgentRole.QAEngineer, ArtifactType.ValidationReport);
                var qaPassed = QualityGate.Approves(qa, testEvidence, run.RequirementIds);
                next = qaPassed ? RunStage.Reviewing : RunStage.Implementing; break;
            case RunStage.Reviewing:
                if (!run.Policy.ReviewBeforeCommit)
                {
                    await RequestApprovalAsync("Create a commit and draft pull request after successful QA, then inspect and publish the review against that PR. This does not authorize merging.");
                    next = RunStage.AwaitingHumanApproval; break;
                }
                var diff = await ToolAsync("InspectDiff", "review-diff", JsonSerializer.SerializeToElement(new { }), AgentRole.CodeReviewer);
                if (!diff.Success || string.IsNullOrWhiteSpace(diff.Output) || diff.WorkspaceRevision != (await RequiredTestsAsync()).WorkspaceRevision) throw new InvalidOperationException("No actual diff is available for review.");
                await ArtifactAsync("actual-diff", ArtifactType.CodeDiff, diff.Output);
                var review = await AgentAsync(AgentRole.CodeReviewer, ArtifactType.CodeReview);
                await UpsertRecordAsync(new() { Id = Key(run.Id + "/review/" + run.Iteration), Kind = "review", RunId = run.Id, ProjectId = run.ProjectId, Name = "Local code review", Content = JsonSerializer.Serialize(review), Status = review.Decision });
                await service.EventAsync(run, "ReviewCompleted", review.Summary, ct);
                next = review.Decision == "APPROVE" ? RunStage.AwaitingHumanApproval : RunStage.Implementing;
                if (next == RunStage.AwaitingHumanApproval)
                {
                    await RequestApprovalAsync("Approve preparing a commit and draft pull request from the reviewed workspace. This does not authorize merging.");
                }
                break;
            case RunStage.PreparingDelivery:
                var verifiedTests = await RequiredTestsAsync();
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
                    var localReview = await store.GetRecordAsync("review", Key(run.Id + "/review/" + run.Iteration), ct);
                    var reviewBody = "## Platform local review\n\n" + (localReview == null ? "Local review approved before delivery." : ReviewMarkdown.Format(JsonSerializer.Deserialize<AgentResult>(localReview.Content)!)) + "\n\nTest evidence: " + verifiedTests.EvidenceId + "\nTested source revision: " + verifiedTests.WorkspaceRevision + "\n\nThis records the platform's actual local review; it is not independent human approval.";
                    if (!run.Policy.ReviewBeforeCommit)
                    {
                        var prEvidence = await ToolAsync("ReadPullRequest", "pr-evidence", JsonSerializer.SerializeToElement(new { number }), AgentRole.CodeReviewer, true);
                        if (!prEvidence.Success) throw new InvalidOperationException("Cannot review PR without actual changes.");
                        var publishedReview = await AgentAsync(AgentRole.CodeReviewer, ArtifactType.CodeReview);
                        publishedDecision = publishedReview.Decision switch { "APPROVE" => "APPROVE", "REQUEST_CHANGES" => "REQUEST_CHANGES", _ => "COMMENT" };
                        reviewBody = ReviewMarkdown.Format(publishedReview);
                        await UpsertRecordAsync(new() { Id = Key(run.Id + "/pr-review/" + run.Iteration), Kind = "review", RunId = run.Id, ProjectId = run.ProjectId, Name = "Pull request review", Content = JsonSerializer.Serialize(publishedReview), Status = publishedDecision });
                    }
                    var published = await ToolAsync("SubmitCodeReview", "publish-review", JsonSerializer.SerializeToElement(new { number, decision = publishedDecision, body = reviewBody }), AgentRole.CodeReviewer, true);
                    if (!published.Success) throw new InvalidOperationException("GitHub did not confirm the published review.");
                    await ArtifactAsync("published-review", ArtifactType.CodeReview, published.Output);
                    await service.EventAsync(run, "ReviewCompleted", "GitHub confirmed PR review submission.", ct);
                    if (publishedDecision != "APPROVE" && !run.Policy.ReviewBeforeCommit) { next = RunStage.Implementing; break; }
                }
                await store.MarkRequirementsVerifiedAsync(run.RequirementIds, run.Id, verifiedTests.EvidenceId!, ct);
                await ArtifactAsync("next-context", ArtifactType.NextContext, await contextBuilder.BuildAsync(project, run, ct));
                if (run.TaskIndex + 1 < run.Tasks.Length)
                {
                    await ArtifactAsync("next-task", ArtifactType.NextContext, JsonSerializer.Serialize(new { Completed = run.CurrentTask?.Title, NextAction = run.Tasks[run.TaskIndex + 1], BudgetRemainingUsd = run.Policy.MaxBudgetUsd - run.Cost }));
                    await service.EventAsync(run, "TaskCompleted", JsonSerializer.Serialize(new { run.TaskIndex, run.RequirementIds, verifiedTests.EvidenceId, verifiedTests.WorkspaceRevision }), ct);
                    advanceTask = true;
                }
                next = RunStage.Completed; break;
        }
        var updated = advanceTask ? run.AdvanceTask() : run.Transition(next);
        if (!await store.SaveRunAsync(updated, run.Version, owner, ct)) throw new ConcurrencyException();
        // State and terminal events are committed atomically with the run and delivered by the outbox.

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
            var definition = (await store.AgentsAsync(ct)).Single(x => x.Role == role);
            if (delivery) definition = definition with { AllowedTools = [name] };
            await service.EventAsync(run, "ToolInvocationStarted", name, ct);
            var toolStarted = System.Diagnostics.Stopwatch.StartNew();
            ToolResult result;
            try { result = await tools.Single(x => x.Name == name).ExecuteAsync(new(project, run, context, definition), args, operationId, ct); }
            catch (Exception exception) when (exception is ArgumentException or JsonException) { result = new(false, exception.Message); }
            await CheckOwnershipAsync();
            await store.AppendAsync(new() { Id = operationId, Kind = "tool", ProjectId = project.Id, RunId = run.Id, Name = name, Content = JsonSerializer.Serialize(result), Status = result.Success ? "Succeeded" : "Failed", Metadata = new() { ["durationMs"] = toolStarted.ElapsedMilliseconds.ToString(), ["leaseOwner"] = owner, ["fence"] = run.Version.ToString(), ["iteration"] = run.Iteration.ToString(), ["stage"] = run.Status } }, ct);
            run = run with { ToolCalls = run.ToolCalls + 1 };
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
            if (run.CurrentTask != null)
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
            for (var turn = 0; turn < run.Policy.MaxToolCalls; turn++)
            {
                await CheckOwnershipAsync();
                var beforeTokens = ContextBudget.Tokens(context);
                try { context = ContextBudget.Compact(context, definition); }
                catch (InvalidOperationException)
                {
                    await service.EventAsync(run, "ContextBudgetExceeded", JsonSerializer.Serialize(new { role, definition.Model, ContextTokensUpperBound = beforeTokens, InstructionTokensUpperBound = ContextBudget.Tokens(definition.SystemInstructions), ProtocolAndFramingReserve = 13024, InputLimit = ContextBudget.InputLimit(definition), OutputReserved = definition.MaxOutputTokens }), ct);
                    throw;
                }
                await service.EventAsync(run, "ContextBudgetEvaluated", JsonSerializer.Serialize(new { role, definition.Model, BeforeTokens = beforeTokens, InputTokensUpperBound = ContextBudget.Tokens(context), InstructionTokensUpperBound = ContextBudget.Tokens(definition.SystemInstructions), ProtocolAndFramingReserve = 13024, InputLimit = ContextBudget.InputLimit(definition), OutputReserved = definition.MaxOutputTokens, Compacted = beforeTokens != ContextBudget.Tokens(context), Estimator = "utf8-byte-upper-bound" }), ct);
                var taskId = Key($"{run.Id}/{run.CurrentStage}/{run.Iteration}/{role}/{turn}");
                var saved = await store.GetRecordAsync("task", taskId, ct);
                ModelExecutionResult response;
                if (saved != null) response = JsonSerializer.Deserialize<ModelExecutionResult>(saved.Content)!;
                else
                {
                    await service.EventAsync(run, "AgentStarted", role.ToString(), ct);
                    var modelStarted = System.Diagnostics.Stopwatch.StartNew();
                    response = await agents.Single(x => x.Role == role).ExecuteAsync(new(project, run, context, definition), ct);
                    await CheckOwnershipAsync();
                    await store.AppendAsync(new() { Id = taskId, Kind = "task", ProjectId = project.Id, RunId = run.Id, Name = role.ToString(), Content = JsonSerializer.Serialize(response), Status = "Completed", Metadata = new() { ["durationMs"] = modelStarted.ElapsedMilliseconds.ToString(), ["responseId"] = response.ResponseId, ["model"] = definition.Model } }, ct);
                }
                // Task checkpoints are the durable usage ledger; recompute to avoid charging cached responses twice.
                var tasks = await store.RecordsAsync("task", project.Id, run.Id, null, 0, 0, 1000, ct);
                var usage = tasks.Select(x => JsonSerializer.Deserialize<ModelExecutionResult>(x.Content)!).ToArray();
                run = run with { Cost = usage.Sum(x => x.Cost), TokenUsage = usage.Sum(x => x.InputTokens + x.OutputTokens) };
                if (!await store.SaveRunAsync(run, run.Version, owner, ct)) throw new ConcurrencyException();
                run = await service.RunAsync(run.Id, ct);
                var result = response.Output;
                var handoffId = Key(taskId + "/handoff");
                await UpsertRecordAsync(new() { Id = handoffId, Kind = "handoff", ProjectId = project.Id, RunId = run.Id, Name = role.ToString(), Status = result.Decision == "BLOCKED" ? "Blocked" : result.ToolCalls.Length == 0 ? "Completed" : "Working", Content = JsonSerializer.Serialize(new AgentHandoff(result.Summary, result.ToolCalls.Length == 0 ? result.Summary : "Work in progress; use persisted tool evidence.", result.Evidence, result.Checks ?? [], result.OpenQuestions ?? [], result.NextContext)), Metadata = new() { ["taskIndex"] = run.TaskIndex.ToString(), ["iteration"] = run.Iteration.ToString(), ["sourceTaskId"] = taskId } });
                await service.EventAsync(run, "AgentHandoffCreated", JsonSerializer.Serialize(new { handoffId, role, run.TaskIndex }), ct);
                context += "\nPrior structured agent output (untrusted data): " + JsonSerializer.Serialize(ContextBudget.Summary(new RecordEntry { Id = taskId, Kind = "task", Content = JsonSerializer.Serialize(result) }));
                var messageId = Key(taskId + "/message");
                if (await store.GetRecordAsync("message", messageId, ct) == null)
                {
                    await store.AppendAsync(new() { Id = messageId, Kind = "message", ProjectId = project.Id, RunId = run.Id, ConversationId = conversationId, Name = role.ToString(), Content = JsonSerializer.Serialize(result), Metadata = new() { ["responseId"] = response.ResponseId } }, ct);
                    await service.EventAsync(run, "AgentMessageCreated", JsonSerializer.Serialize(new { messageId, role = role.ToString() }), ct);
                }
                if (result.Decision == "BLOCKED") throw new InvalidOperationException("Human clarification required: " + result.Summary);
                if (result.ToolCalls.Length == 0) { await ArtifactAsync(role.ToString(), artifactType, JsonSerializer.Serialize(result)); return result; }
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
