using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;

public sealed partial class RuntimeFlowTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    [Theory]
    [InlineData(AgentRole.QAEngineer, AgentRole.BackendEngineer)]
    [InlineData(AgentRole.QAEngineer, AgentRole.FrontendEngineer)]
    [InlineData(AgentRole.CodeReviewer, AgentRole.BackendEngineer)]
    [InlineData(AgentRole.CodeReviewer, AgentRole.FrontendEngineer)]
    public async Task ReworkRunsOnlyResponsibleDeveloperThenRepeatsTestsQaAndReview(AgentRole reviewer, AgentRole target)
    {
        await using var root = new PlatformFactory();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(new RejectOnce(reviewer, target));
        }));
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var run = await Start(client, store, ExecutionPresets.StandardDevelopment);
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.AwaitingHumanApproval, run.CurrentStage);
        Assert.Equal(2, run.Iteration);
        var transition = Assert.Single(run.ReworkPlan);
        Assert.Equal(target, transition.TargetAgent); Assert.Equal(reviewer, transition.SourceAgent);
        var tasks = await store.RecordsAsync("task", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        var corrections = tasks.Where(x => x.Metadata.GetValueOrDefault("iteration") == "2").ToArray();
        Assert.Contains(corrections, x => x.Name == target.ToString());
        Assert.DoesNotContain(corrections, x => x.Name == (target == AgentRole.BackendEngineer ? "FrontendEngineer" : "BackendEngineer"));
        Assert.Contains(corrections, x => x.Name == "QAEngineer"); Assert.Contains(corrections, x => x.Name == "CodeReviewer");
        var tools = await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.Equal(2, tools.Count(x => x.Name == "RunTests"));
        // A new store instance recovers the explicit plan and counters; no in-memory graph state.
        var recovered = await new MongoPlatformStore(Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028", root.Database).GetRunAsync(run.Id, default);
        Assert.Equal(JsonSerializer.Serialize(run.ReworkPlan), JsonSerializer.Serialize(recovered!.ReworkPlan)); Assert.Equal(tools.Count, recovered.ToolCalls);
    }
    [Fact]
    public async Task ProviderFailureAndExplicitResumePreserveSuccessfulWritesAndDeliveryIds()
    {
        await using var root = new PlatformFactory();
        var sandbox = new DeterministicSandbox();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(new FailAfterWrite());
            services.RemoveAll<IExecutionSandbox>(); services.AddSingleton<IExecutionSandbox>(sandbox);
        }));
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var run = await Start(client, store, ExecutionPresets.StandardDevelopment);
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.Failed, run.CurrentStage);
        Assert.Equal("OpenAIMalformedJson", run.FailureCategory);
        Assert.Contains("MalformedJson", run.FailureReason);
        var before = await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        var write = Assert.Single(before, x => x.Name == "WriteFile");
        var artifacts = await store.RecordsAsync("artifact", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.DoesNotContain(before, x => x.Name is "CreateCommit" or "CreatePullRequest");
        (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/retry", new { })).EnsureSuccessStatusCode();
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.AwaitingHumanApproval, run.CurrentStage);
        var after = await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.Equal(write.Content, Assert.Single(after, x => x.Id == write.Id).Content);
        Assert.Equal(3, sandbox.Operations.Count(x => x == "WriteFile"));
        var afterArtifacts = await store.RecordsAsync("artifact", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.All(artifacts, artifact => Assert.Contains(afterArtifacts, x => x.Id == artifact.Id && x.Content == artifact.Content));
        var approvals = await store.RecordsAsync("approval", run.ProjectId, run.Id, null, 0, 0, 100, default);
        var approval = Assert.Single(approvals, x => x.Status == "Pending");
        (await client.PostAsJsonAsync($"/api/v1/approvals/{approval.Id}/decision", new { approve = true, reason = "Test-only delivery approval" })).EnsureSuccessStatusCode();
        run = await Wait(store, run.Id, r => r.CurrentStage is RunStage.Completed or RunStage.Failed or RunStage.Paused);
        Assert.Equal(RunStage.Completed, run.CurrentStage);
        var delivered = await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.Single(delivered, x => x.Name == "CreateCommit");
        Assert.Single(delivered, x => x.Name == "CreatePullRequest");
        Assert.All(before, entry => Assert.Single(delivered, x => x.Id == entry.Id && x.Content == entry.Content));
    }
    private sealed class FailAfterWrite : ILanguageModelProvider
    {
        private bool failed;
        private readonly DeterministicModel inner = new();
        public Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
        {
            if (!failed && context.Definition.Role == AgentRole.BackendEngineer && context.Context.Contains("written backend/Feature.cs"))
            {
                failed = true;
                throw new ProviderException(new(context.Definition.Model, "resp_failed", "req_failed", "completed", null, ProviderErrorCategory.MalformedJson, 100, 50, 1));
            }
            return inner.ExecuteAsync(context, ct);
        }
    }
    [Fact]
    public async Task PublishedReviewReworkTargetsDeveloperAndRequiresAnotherOwnerDecision()
    {
        await using var root = new PlatformFactory();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(new RejectOnce(AgentRole.CodeReviewer, AgentRole.FrontendEngineer));
        }));
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var run = await Start(client, store, ExecutionPresets.StandardDevelopment with { ReviewBeforeCommit = false });
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.AwaitingHumanApproval, run.CurrentStage);
        var approvals = await store.RecordsAsync("approval", run.ProjectId, run.Id, null, 0, 0, 100, default);
        var approval = Assert.Single(approvals, x => x.Status == "Pending");
        (await client.PostAsJsonAsync($"/api/v1/approvals/{approval.Id}/decision", new { approve = true, reason = "Owner approves fixture draft delivery" })).EnsureSuccessStatusCode();
        run = await Wait(store, run.Id, r => r.Iteration == 2 && !r.IsRunnable || r.CurrentStage is RunStage.Failed or RunStage.Paused);
        Assert.Equal(RunStage.AwaitingHumanApproval, run.CurrentStage);
        Assert.Equal(AgentRole.FrontendEngineer, Assert.Single(run.ReworkPlan).TargetAgent);
        var tasks = await store.RecordsAsync("task", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.Contains(tasks, x => x.Name == "FrontendEngineer" && x.Metadata.GetValueOrDefault("iteration") == "2");
        Assert.DoesNotContain(tasks, x => x.Name == "BackendEngineer" && x.Metadata.GetValueOrDefault("iteration") == "2");
    }
    [Fact]
    public async Task OwnerExtensionPreservesOperationIdsAndContinuesCheckpointedStage()
    {
        await using var factory = new PlatformFactory();
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var run = await Start(client, store, ExecutionPresets.QuickFix with { MaxToolCalls = 5 });
        run = await Wait(store, run.Id, r => r.CurrentStage == RunStage.Paused);
        var before = await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        var modelBefore = await store.RecordsAsync("task", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        var response = await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/limits", new { policy = ExecutionPresets.StandardDevelopment, reason = "Owner approves completing fixture", version = run.Version });
        response.EnsureSuccessStatusCode();
        run = (await store.GetRunAsync(run.Id, default))!;
        Assert.Equal(RunStage.Paused, run.CurrentStage); Assert.Single(run.LimitExtensions);
        (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/resume", new { })).EnsureSuccessStatusCode();
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.AwaitingHumanApproval, run.CurrentStage);
        var after = await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.All(before, entry => Assert.Single(after, x => x.Id == entry.Id));
        var modelsAfter = await store.RecordsAsync("task", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.All(modelBefore, entry => Assert.Single(modelsAfter, x => x.Id == entry.Id));
        Assert.Equal(after.Count, run.ToolCalls);
        Assert.Equal(after.Count, run.Usage.Sum(x => x.ToolInvocations));
        Assert.Equal(modelsAfter.Count, run.Usage.Sum(x => x.ModelRequests));
        Assert.Equal(1, after.Count(x => x.Name == "RunTests"));
    }
    [Fact]
    public async Task FailedToolAndItsRetryHaveSeparateDurableUsage()
    {
        await using var root = new PlatformFactory();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<IExecutionSandbox>(); services.AddSingleton<IExecutionSandbox>(new FailOneWrite());
        }));
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var run = await Start(client, store, ExecutionPresets.StandardDevelopment);
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.AwaitingHumanApproval, run.CurrentStage);
        Assert.Equal(1, run.Usage.Sum(x => x.FailedToolInvocations));
        Assert.Equal(1, run.Usage.Sum(x => x.RetriedToolInvocations));
    }
    private sealed class FailOneWrite : IExecutionSandbox
    {
        private readonly DeterministicSandbox inner = new();
        private bool failed;
        public bool Configured => true;
        public Task<SandboxResponse> ExecuteAsync(SandboxRequest request, CancellationToken ct)
        {
            if (!failed && request.Operation == "WriteFile") { failed = true; return Task.FromResult(new SandboxResponse(false, "Test fixture transient failure before write", "failed-evidence", 1, request.RunId)); }
            return inner.ExecuteAsync(request, ct);
        }
    }
    [Fact]
    public void RedactionRemovesEnvironmentSecretsPrivateKeysAndCredentialUrls()
    {
        var secret = "OPENAI_API_KEY=unusual-secret-value GITHUB_TOKEN=another-value https://user:password@example.test/repo -----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----";
        var redacted = SecretRedactor.Redact(secret);
        Assert.DoesNotContain("unusual-secret-value", redacted); Assert.DoesNotContain("another-value", redacted); Assert.DoesNotContain("user:password", redacted); Assert.DoesNotContain("abc", redacted);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeparateStreamsRemainBoundedAcrossRemoteTransport(bool overflow)
    {
        var text = new string('x', overflow ? 100001 : 99000);
        var response = new SandboxResponse(true, text[..Math.Min(text.Length, 99000)], "actual-evidence", 0, "run", "revision", new("node", ["node", "--test"], "/workspace", text, ""));
        var sandbox = new RemoteExecutionSandbox(new HttpClient(new StaticResponse(response)), "https://sandbox.example.invalid/", "test-only-token");
        var request = new SandboxRequest("op", "run", new("owner", "repo", 1), "RunTests", JsonSerializer.SerializeToElement(new { profile = "node" }));
        if (overflow) await Assert.ThrowsAsync<ExecutorFailureException>(() => sandbox.ExecuteAsync(request, default));
        else Assert.Equal(text, (await sandbox.ExecuteAsync(request, default)).Execution!.Stdout);
    }
    private sealed class StaticResponse(SandboxResponse response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = JsonContent.Create(response) });
    }
    private static async Task Login(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { password = PlatformFactory.Password })).EnsureSuccessStatusCode();
    }
    private static async Task<ExecutionRun> Start(HttpClient client, IPlatformStore store, ExecutionPolicy policy, bool planning = false)
    {
        var project = new Project { Name = "Runtime fixture", RepositoryConnection = new("owner", "fixture", 1) };
        await store.SaveProjectAsync(project, null, default);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/tasks", new { policy, tasks = new[] { new EngineeringTask("Answer", "Correct the fixture", ["Tests pass"], RequiresBusinessAnalysis: planning, RequiresArchitecture: planning, Developers: [AgentRole.BackendEngineer, AgentRole.FrontendEngineer]) } });
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<ExecutionRun>(Json))!;
    }
    private static async Task<ExecutionRun> Wait(IPlatformStore store, string id, Func<ExecutionRun, bool> done)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (true) { var run = (await store.GetRunAsync(id, timeout.Token))!; if (done(run)) return run; await Task.Delay(100, timeout.Token); }
    }
    private sealed class RejectOnce(AgentRole reviewer, AgentRole target) : ILanguageModelProvider
    {
        private readonly DeterministicModel inner = new();
        public async Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
        {
            var response = await inner.ExecuteAsync(context, ct);
            return context.Definition.Role == reviewer && context.Run.Iteration == 1 ? response with { Output = response.Output with { Decision = "REQUEST_CHANGES", Summary = "Correct responsible developer output", ReworkTargets = [target.ToString()], Findings = [new("High", "fixture", 1, "Fixture issue", "Correct fixture")] } } : response;
        }
    }
}
