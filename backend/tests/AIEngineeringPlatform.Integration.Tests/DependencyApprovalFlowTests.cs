using Microsoft.AspNetCore.Hosting;
using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Application.Dependencies;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;
public sealed class DependencyApprovalFlowTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public static DependencyProposal Proposal => new(1, "npm", "npm", "https://registry.npmjs.org", [new("is-number", "7.0.0", false, "Numeric contract validation")], [], "License/vulnerability status unknown", ["Built-in checks"], ["Backend work"], ["Frontend validation"], []);
    [Fact]
    public async Task OwnerDecisionsAreDurableSelectedScopedAndResumeTheSameCheckpoint()
    {
        await using var fixture = new PlatformFactory(); var executor = new TestDependencyExecutor();
        await using var app = fixture.WithWebHostBuilder(builder => { builder.UseSetting("WORKER_ENABLED", "false"); builder.ConfigureServices(services => { services.RemoveAll<IDependencyExecutor>(); services.AddSingleton<IDependencyExecutor>(executor); }); });
        using var client = app.CreateClient(); client.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { password = PlatformFactory.Password })).EnsureSuccessStatusCode();
        var store = app.Services.GetRequiredService<IPlatformStore>();
        var project = new Project { Name = "Dependency governance fixture", RepositoryConnection = new("test-owner", "test-repo", 1) };
        Assert.True(await store.SaveProjectAsync(project, null, default));
        var source = new ExecutionRun { ProjectId = project.Id, ProjectSnapshot = project, IdempotencyKey = Guid.NewGuid().ToString("N"), CurrentStage = RunStage.Planning, Cost = 0.2m, ToolCalls = 26, Policy = new(MaxToolCalls: 100) };
        var waiting = source.Transition(RunStage.AwaitingDependencyApproval);
        await store.CreateRunAsync(waiting, default);
        await store.AppendAsync(new() { Id = "prior-business-analysis", Kind = "artifact", ProjectId = project.Id, RunId = waiting.Id, Name = "Requirements", Content = "Preserved completed business analysis" }, default);
        var service = app.Services.GetRequiredService<DependencyGovernance>();
        var definition = new AgentDefinition("Architect", AgentRole.Architect, "Architect", "", "fixture", "", [], ["RequestDependencyApproval"]);
        var requested = await service.RequestAsync(new(project, source, "", definition, "origin-task"), JsonSerializer.SerializeToElement(Proposal, DependencyGovernance.StrictJson), "origin-operation", default);
        var path = "/api/v1/dependency-approvals/" + requested.Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path + "/decision", new { version = 0, action = "Resolve", packageIds = Array.Empty<string>(), registryAccess = false, reason = "No network approved" })).StatusCode);
        Assert.Equal(0, executor.Resolutions);
        var response = await client.PostAsJsonAsync(path + "/decision", new { version = 0, action = "Resolve", packageIds = Array.Empty<string>(), registryAccess = true, reason = "Approve bounded public registry metadata and tarballs" }); response.EnsureSuccessStatusCode();
        var ready = (await response.Content.ReadFromJsonAsync<DependencyApprovalRequest>(Json))!;
        Assert.Equal(DependencyApprovalStatus.ReadyForReview, ready.Status); Assert.Equal(1, executor.Resolutions); Assert.Equal(0, executor.Grants);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path + "/decision", new { version = 0, action = "Reject", packageIds = Array.Empty<string>(), registryAccess = false, reason = "Stale tab" })).StatusCode);
        response = await client.PostAsJsonAsync(path + "/decision", new { version = ready.Version, action = "Approve", packageIds = new[] { "is-number@7.0.0" }, registryAccess = false, reason = "Approve selected direct package" }); response.EnsureSuccessStatusCode();
        var partial = (await response.Content.ReadFromJsonAsync<DependencyApprovalRequest>(Json))!; Assert.Equal(DependencyApprovalStatus.ReadyForReview, partial.Status); Assert.Equal(0, executor.Grants);
        response = await client.PostAsJsonAsync(path + "/decision", new { version = partial.Version, action = "Approve", packageIds = new[] { "kind-of@6.0.3" }, registryAccess = false, reason = "Approve remaining transitive package" }); response.EnsureSuccessStatusCode();
        var approved = (await response.Content.ReadFromJsonAsync<DependencyApprovalRequest>(Json))!; Assert.Equal(DependencyApprovalStatus.Approved, approved.Status); Assert.Equal(1, executor.Grants);
        var freshStore = new MongoDependencyApprovalStore(Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028", fixture.Database);
        var persisted = (await freshStore.GetAsync(requested.Id, default))!; Assert.Equal(3, persisted.Audit.Length); Assert.All(persisted.Audit, x => Assert.False(string.IsNullOrWhiteSpace(x.Owner)));
        (await client.PostAsJsonAsync(path + "/resume", new { })).EnsureSuccessStatusCode();
        var resumed = (await store.GetRunAsync(source.Id, default))!; Assert.Equal(RunStage.Planning, resumed.CurrentStage); Assert.Equal(26, resumed.ToolCalls); Assert.Equal(0.2m, resumed.Cost); Assert.Equal(source.Policy, resumed.Policy); Assert.Equal(source.StartedAt, resumed.StartedAt); Assert.Equal(1, resumed.Iteration);
        Assert.Equal("Preserved completed business analysis", (await store.GetRecordAsync("artifact", "prior-business-analysis", default))!.Content);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
    }
    [Fact]
    public async Task DependencyRecordsAreProjectFilteredAndRejectUnknownContractFields()
    {
        await using var fixture = new PlatformFactory(); using var client = fixture.CreateClient(); client.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { password = PlatformFactory.Password })).EnsureSuccessStatusCode();
        var store = fixture.Services.GetRequiredService<IDependencyApprovalStore>();
        var request = new DependencyApprovalRequest(new string('a', 64), "project-a", "run-a", RunStage.Planning, AgentRole.Architect, "task", "operation", 0, Proposal, DependencyApprovalStatus.Pending, 0, DateTimeOffset.UtcNow, []);
        Assert.True(await store.SaveAsync(request, null, default)); Assert.False(await store.SaveAsync(request, null, default));
        Assert.Empty((await client.GetFromJsonAsync<DependencyRequestSummary[]>("/api/v1/dependency-approvals?projectId=project-b", Json))!);
        Assert.Single((await client.GetFromJsonAsync<DependencyRequestSummary[]>("/api/v1/dependency-approvals?projectId=project-a", Json))!);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DependencyProposal>("{\"version\":1,\"grantNetwork\":true}", DependencyGovernance.StrictJson));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task V2WorkerPausesOnceAndReusesBusinessAnalysisAfterAnOwnerRejection(bool legacyBlockedCheckpoint)
    {
        await using var fixture = new PlatformFactory(); var model = new DependencyRequestModel(legacyBlockedCheckpoint);
        await using var app = fixture.WithWebHostBuilder(builder => { builder.UseSetting("AGENT_RUNTIME_V2_NEW_RUNS", "true"); builder.ConfigureServices(services => { services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(model); }); });
        using var client = app.CreateClient(); client.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { password = PlatformFactory.Password })).EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync("/api/v1/projects", new { name = "V2 dependency checkpoint fixture", description = "Deterministic contract verification", originalContext = "Implement the bounded fixture", technologyStack = new[] { ".NET", "React" } }); response.EnsureSuccessStatusCode();
        var project = (await response.Content.ReadFromJsonAsync<Project>(Json))!;
        (await client.PostAsJsonAsync("/api/v1/integrations/github/repositories", new { projectId = project.Id, connection = new RepositoryConnection("test-owner", "test-repo", 1) })).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        response = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/runs", new { }); response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<ExecutionRun>(Json))!;
        async Task<ExecutionRun> WaitForAsync(RunStage stage)
        {
            for (var i = 0; i < 300; i++) { var current = (await client.GetFromJsonAsync<ExecutionRun>("/api/v1/runs/" + created.Id, Json))!; if (current.CurrentStage == stage) return current; if (current.CurrentStage is RunStage.Failed or RunStage.Paused) throw new Exception(current.FailureReason); await Task.Delay(100); }
            throw new TimeoutException("Dependency fixture did not reach " + stage);
        }
        if (legacyBlockedCheckpoint)
        {
            var oldBlocked = await WaitForAsync(RunStage.Paused);
            Assert.Equal(1, model.ArchitectCalls);
            (await client.PostAsJsonAsync("/api/v1/runs/" + created.Id + "/resume", new { })).EnsureSuccessStatusCode();
        }
        var waiting = await WaitForAsync(RunStage.AwaitingDependencyApproval); Assert.Equal(2, waiting.AgentRuntimeVersion); Assert.Equal(RunStage.Planning, waiting.ResumeStage);
        var approvals = (await client.GetFromJsonAsync<DependencyRequestSummary[]>("/api/v1/dependency-approvals?runId=" + created.Id, Json))!;
        var request = Assert.Single(approvals); Assert.Equal(AgentRole.Architect, request.Agent); Assert.Equal(1, model.BusinessAnalysisCalls);
        var store = app.Services.GetRequiredService<IPlatformStore>();
        var tools = await store.RecordsAsync("tool", project.Id, waiting.Id, null, 0, 0, 100, default); Assert.Single(tools, x => x.Name == "RequestDependencyApproval");
        var messageRecords = await store.RecordsAsync("agentMessageV2", project.Id, waiting.Id, null, 0, 0, 100, default);
        Assert.Contains(messageRecords, x => x.Content.Contains("DependencyApprovalRequest", StringComparison.Ordinal));
        await Task.Delay(300); Assert.Equal(legacyBlockedCheckpoint ? 2 : 1, model.ArchitectCalls);
        (await client.PostAsJsonAsync($"/api/v1/dependency-approvals/{request.Id}/decision", new { version = request.Version, action = "Reject", packageIds = Array.Empty<string>(), registryAccess = false, reason = "Use the existing built-in validation alternative" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/v1/dependency-approvals/{request.Id}/resume", new { })).EnsureSuccessStatusCode();
        var delivery = await WaitForAsync(RunStage.AwaitingHumanApproval);
        Assert.Equal(1, model.BusinessAnalysisCalls); Assert.Equal(legacyBlockedCheckpoint ? 3 : 2, model.ArchitectCalls); Assert.Equal(waiting.StartedAt, delivery.StartedAt); Assert.Equal(waiting.Policy, delivery.Policy); Assert.Equal(1, delivery.Iteration);
        tools = await store.RecordsAsync("tool", project.Id, waiting.Id, null, 0, 0, 100, default); Assert.Single(tools, x => x.Name == "RequestDependencyApproval");
        Assert.Single(await client.GetFromJsonAsync<DependencyRequestSummary[]>("/api/v1/dependency-approvals?runId=" + created.Id, Json) ?? []);
    }
    private sealed class DependencyRequestModel(bool legacyBlockedCheckpoint = false) : ILanguageModelProvider
    {
        public int BusinessAnalysisCalls, ArchitectCalls;
        private readonly DeterministicModel fallback = new();
        public async Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
        {
            var response = await fallback.ExecuteAsync(context, ct);
            if (context.Definition.Role == AgentRole.BusinessAnalyst) Interlocked.Increment(ref BusinessAnalysisCalls);
            if (context.Definition.Role != AgentRole.Architect) return response;
            var attempt = Interlocked.Increment(ref ArchitectCalls);
            if (legacyBlockedCheckpoint && attempt == 1) return response with { Output = response.Output with { Decision = "BLOCKED", Summary = "Legacy dependency catalog gap; no dependency authorization workflow was available", ToolCalls = [] } };
            if (context.Context.Contains("Verified owner dependency decision:", StringComparison.Ordinal)) return response;
            return response with { Output = response.Output with { Summary = "Exact dependency requires owner authorization", ToolCalls = [new("RequestDependencyApproval", JsonSerializer.SerializeToElement(Proposal, DependencyGovernance.StrictJson))] } };
        }
    }
    private sealed class TestDependencyExecutor : IDependencyExecutor
    {
        public int Resolutions, Grants;
        public Task<DependencyResolution> ResolveAsync(DependencyApprovalRequest request, RepositoryConnection repository, CancellationToken ct)
        {
            Resolutions++;
            var plan = new DependencyResolution(request.Id, request.ProjectId, request.RunId, repository, new('a', 64), new('b', 64), "{}", "{}", new('c', 64), [new("is-number", "7.0.0", "sha512-" + Convert.ToBase64String(new byte[64]), "https://registry.npmjs.org", true, false, "Unknown", "Unknown", false), new("kind-of", "6.0.3", "sha512-" + Convert.ToBase64String(new byte[64]), "https://registry.npmjs.org", false, false, "Unknown", "Unknown", false)], new('d', 64));
            return Task.FromResult(plan with { PlanSha256 = DependencyGovernance.ResolutionFingerprint(plan) });
        }
        public Task<DependencyResolution?> ResolutionAsync(string id, CancellationToken ct) => Task.FromResult<DependencyResolution?>(null);
    public Task<DependencyResolution?> ReconcileAsync(string id, CancellationToken ct) => ResolutionAsync(id, ct);
        public Task GrantAsync(DependencyGrant grant, CancellationToken ct) { Grants++; return Task.CompletedTask; }
    }
}
