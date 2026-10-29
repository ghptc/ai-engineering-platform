using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using System.Collections.Concurrent;
using System.Text.Json;
namespace AIEngineeringPlatform.Integration.Tests;
// These adapters exist only in test assemblies and cannot be selected by production configuration.
public sealed class DeterministicModel(bool alreadySatisfied = false) : ILanguageModelProvider
{
    public Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var role = context.Definition.Role;
        var file = role switch { AgentRole.BackendEngineer => "backend/Feature.cs", AgentRole.FrontendEngineer => "frontend/Feature.tsx", _ => "backend/FeatureTests.cs" };
        ToolCall[] calls = [];
        if (!alreadySatisfied && role is AgentRole.BackendEngineer or AgentRole.FrontendEngineer or AgentRole.TestEngineer && !context.Context.Contains("written " + file)) calls = [new("WriteFile", JsonSerializer.SerializeToElement(new { path = file, content = "// deterministic test fixture: " + role + " task " + context.Run.TaskIndex }))];
        var evidence = System.Text.RegularExpressions.Regex.Matches(context.Context, "fake-evidence-[a-f0-9]{64}").Select(x => x.Value).Distinct().ToArray();
        var tests = evidence.LastOrDefault() ?? "test-fixture-evidence";
        // Resolve the required-test report from the persisted context, whose JSON escapes still preserve evidence ids.
        var reportIndex = context.Context.LastIndexOf("TestReport", StringComparison.Ordinal);
        if (reportIndex >= 0) tests = System.Text.RegularExpressions.Regex.Match(context.Context[reportIndex..], "fake-evidence-[a-f0-9]{64}") is { Success: true } match ? match.Value : tests;
        var requiredIndex = context.Context.LastIndexOf("Required test evidence:", StringComparison.Ordinal);
        if (requiredIndex >= 0)
        {
            var requiredMatch = System.Text.RegularExpressions.Regex.Match(context.Context[requiredIndex..], "fake-evidence-[a-f0-9]{64}");
            if (requiredMatch.Success) tests = requiredMatch.Value;
        }
        var requirements = role == AgentRole.BusinessAnalyst ? new[] { new RequirementProposal("Fixture requirement", "Deterministic test scenario", "As an owner, I need traceable delivery.", ["Persist and verify the fixture"], 1, [], false) } : [];
        var decisions = role == AgentRole.Architect ? new[] { new ArchitectureDecision("Test architecture", "Test-only context", "Use deterministic adapters", "External calls are not verified", "") } : [];
        var checks = role == AgentRole.QAEngineer ? context.Run.RequirementIds.Select(id => new QualityCheck(id, "Fixture test execution", true, tests, "Deterministic test evidence only")).ToArray() : [];
        var output = new AgentResult("Deterministic test fixture: " + role, role is AgentRole.QAEngineer or AgentRole.CodeReviewer ? "APPROVE" : "CONTINUE", ["Persist and verify the fixture"], evidence, ["This is test-only simulated model output."], calls, "Next task must use verified artifacts.", requirements, [], checks, [], decisions);
        return Task.FromResult(new ModelExecutionResult(output, "fake-response-" + role, 100, 50, 0.001m));
    }
}
public sealed class DeterministicSandbox(bool alreadySatisfied = false, bool existingPr = false, bool revisionMismatch = false) : IExecutionSandbox
{
    private readonly ConcurrentDictionary<string, string> files = new();
    private readonly ConcurrentDictionary<string, SandboxResponse> results = new();
    public bool Configured => true;
    public List<string> Operations { get; } = [];
    private bool tested;
    public Task<SandboxResponse> ExecuteAsync(SandboxRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(results.GetOrAdd(request.OperationId, _ =>
        {
            Operations.Add(request.Operation);
            var output = "test-only simulated " + request.Operation;
            if (request.Operation == "WriteFile") { var path = request.Arguments.GetProperty("path").GetString()!; files[request.RunId + "/" + path] = request.Arguments.GetProperty("content").GetString()!; output = "written " + path; }
            if (request.Operation == "InspectDiff") output = string.Join('\n', files.Where(x => x.Key.StartsWith(request.RunId + "/")).OrderBy(x => x.Key).Select(x => "diff --git a/" + x.Key + " b/" + x.Key + "\n+" + x.Value));
            if (request.Operation == "ReadRepository") output = JsonSerializer.Serialize(new { HasSourceChanges = !alreadySatisfied, SelectedBranch = existingPr ? "engineering/previous" : null, HeadCommit = new string('a', 40), TestProfiles = new[] { "node" } });
            if (request.Operation == "RunTests") { tested = true; output = "TEST FIXTURE ONLY: simulated 3 passed, 0 failed"; }
            var revision = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', files.Where(x => x.Key.StartsWith(request.RunId + "/")).OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value)))));
            if (tested && revisionMismatch && request.Operation == "InspectDiff") revision = "changed-after-tests";
            return new(true, output, "fake-evidence-" + request.OperationId, 0, request.RunId, revision);
        }));
    }
}
public sealed class DeterministicSourceControl : ISourceControl
{
    public bool Configured => true;
    public bool VerifyWebhook(ReadOnlySpan<byte> payload, string signature) => false;
    public Task<string> ExecuteAsync(RepositoryConnection repo, string operation, JsonElement args, string operationId, CancellationToken ct) => Task.FromResult(operation == "ReconcileRepository" ? JsonSerializer.Serialize(new { pullRequests = new[] { new { number = 7, branch = "engineering/previous", commit = new string('a',40), state = "open", merged = (string?)null, url = "https://example.invalid/pr/7" } } }) : operation == "CreatePullRequest" ? "{\"number\":1,\"html_url\":\"https://example.invalid/test-only-pr\"}" : "{\"testFixture\":true}");
}
