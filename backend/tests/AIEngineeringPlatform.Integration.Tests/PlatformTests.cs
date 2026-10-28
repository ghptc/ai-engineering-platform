using System.Net;
using System.Net.Http.Json;
using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;

public sealed class PlatformTests : IClassFixture<PlatformFactory>
{
    private readonly PlatformFactory factory;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public PlatformTests(PlatformFactory factory) => this.factory = factory;
    private async Task<HttpClient> LoginAsync()
    {
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { password = PlatformFactory.Password }); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return client;
    }
    [Fact]
    public async Task OwnerAuthenticationAndCsrfAreRequired()
    {
        var client = factory.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/auth/login", new { password = PlatformFactory.Password })).StatusCode);
    }
    [Fact]
    public async Task InvalidProjectReturnsProblemDetails()
    {
        using var client = await LoginAsync(); var response = await client.PostAsJsonAsync("/api/v1/projects", new { name = "", description = "", originalContext = "", technologyStack = Array.Empty<string>() }); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
    }
    [Fact]
    public async Task ConversationSurvivesIndependentRequests()
    {
        using var client = await LoginAsync(); var response = await client.PostAsJsonAsync("/api/v1/projects", new { name = "Conversation project", description = "Test", originalContext = "Persist messages", technologyStack = new[] { "C#" } }); response.EnsureSuccessStatusCode(); var project = (await response.Content.ReadFromJsonAsync<Project>(Json))!;
        response = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/conversations", new { }); var conversation = (await response.Content.ReadFromJsonAsync<RecordEntry>(Json))!;
        response = await client.PostAsJsonAsync($"/api/v1/conversations/{conversation.Id}/messages", new { content = "Client acceptance criteria" }); response.EnsureSuccessStatusCode();
        var messages = await client.GetFromJsonAsync<Page<RecordEntry>>($"/api/v1/conversations/{conversation.Id}/messages", Json); Assert.Single(messages!.Items); Assert.Equal("Client acceptance criteria", messages.Items[0].Content);
        var replyResponse = await client.PostAsJsonAsync($"/api/v1/conversations/{conversation.Id}/reply", new { });
        replyResponse.EnsureSuccessStatusCode();
        var reply = (await replyResponse.Content.ReadFromJsonAsync<RecordEntry>(Json))!;
        Assert.Equal("BusinessAnalyst", reply.Name);
        var duplicateReply = await client.PostAsJsonAsync($"/api/v1/conversations/{conversation.Id}/reply", new { });
        Assert.Equal(reply.Id, (await duplicateReply.Content.ReadFromJsonAsync<RecordEntry>(Json))!.Id);
        messages = await client.GetFromJsonAsync<Page<RecordEntry>>($"/api/v1/conversations/{conversation.Id}/messages", Json);
        Assert.Equal(2, messages!.Items.Count);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FullWorkflowPersistsEvidenceAndRequiresApproval(bool reviewBeforeCommit)
    {
        using var client = await LoginAsync(); var created = await client.PostAsJsonAsync("/api/v1/projects", new { name = "Verified workflow", description = "Deterministic external adapters", originalContext = "Create a tested feature", technologyStack = new[] { "C#", "React" }, executionPolicy = new ExecutionPolicy(ReviewBeforeCommit: reviewBeforeCommit) }); created.EnsureSuccessStatusCode(); var project = (await created.Content.ReadFromJsonAsync<Project>(Json))!;
        var store = factory.Services.GetRequiredService<IPlatformStore>(); await store.SaveProjectAsync(project with { RepositoryConnection = new("test-owner", "test-repo", 1), Version = project.Version + 1 }, project.Version, default);
        // Reproduce recursive snapshots larger than the former 160000-character bound.
        await store.AppendAsync(new() { Kind = "artifact", ProjectId = project.Id, Name = "DevelopmentContext", Content = new string('x', 200000) }, default);
        await store.AppendAsync(new() { Kind = "artifact", ProjectId = project.Id, Name = "TestReport", Content = new string('h', 200000), RunId = "completed-history" }, default);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var start = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/runs", new { }); Assert.Equal(HttpStatusCode.Accepted, start.StatusCode); var run = (await start.Content.ReadFromJsonAsync<ExecutionRun>(Json))!;
        var duplicate = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/runs", new { }); Assert.Equal(run.Id, (await duplicate.Content.ReadFromJsonAsync<ExecutionRun>(Json))!.Id);
        run = await WaitAsync(client, run.Id, RunStage.AwaitingHumanApproval); Assert.True(run.TokenUsage > 0); Assert.True(run.Cost > 0);
        var artifacts = await client.GetFromJsonAsync<Page<RecordEntry>>($"/api/v1/runs/{run.Id}/artifacts", Json); Assert.Contains(artifacts!.Items, x => x.Name == "TestReport"); if (reviewBeforeCommit) Assert.Contains(artifacts.Items, x => x.Name == "CodeReview");
        var approvals = await client.GetFromJsonAsync<Page<RecordEntry>>("/api/v1/approvals", Json); var approval = Assert.Single(approvals!.Items, x => x.RunId == run.Id);
        var decision = await client.PostAsJsonAsync($"/api/v1/approvals/{approval.Id}/decision", new { approve = true, reason = "Verified deterministic fixture" }); Assert.Equal(HttpStatusCode.Accepted, decision.StatusCode);
        await WaitAsync(client, run.Id, RunStage.Completed);
        // The transition and its outbox are atomic; delivery to the replay feed is asynchronous.
        Page<RecordEntry>? events = null;
        using var eventDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        do
        {
            events = await client.GetFromJsonAsync<Page<RecordEntry>>($"/api/v1/runs/{run.Id}/events?limit=500", Json, eventDeadline.Token);
            if (events!.Items.Any(x => x.Name == "RunCompleted")) break;
            await Task.Delay(50, eventDeadline.Token);
        } while (true);
        Assert.Contains(events.Items, x => x.Name == "ContextInventory"); Assert.Contains(events.Items, x => x.Name == "ContextBudgetEvaluated");
        Assert.Contains(events.Items, x => x.Name == "RunCompleted"); Assert.True(events.Items.Select(x => x.Sequence).SequenceEqual(events.Items.Select(x => x.Sequence).Order()));
    }
    [Fact]
    public async Task MongoLeasesFenceStaleWorkersAndEnforceIdempotency()
    {
        var store = new MongoPlatformStore(Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028", "aep_lease_" + Guid.NewGuid().ToString("N")); await store.InitializeAsync(default);
        var run = new ExecutionRun { ProjectId = "p", IdempotencyKey = "key" }; await store.CreateRunAsync(run, default); Assert.Equal(run.Id, (await store.CreateRunAsync(run with { Id = Guid.NewGuid().ToString("N") }, default)).Id);
        var first = await store.ClaimAsync("one", TimeSpan.FromMilliseconds(5), default); Assert.NotNull(first); await Task.Delay(30); var second = await store.ClaimAsync("two", TimeSpan.FromMinutes(1), default); Assert.NotNull(second);
        Assert.False(await store.SaveRunAsync(first!, first!.Version, "one", default)); Assert.True(await store.SaveRunAsync(second!, second!.Version, "two", default));
    }
    [Fact]
    public async Task CommittedTransitionSurvivesRestartBeforeEventDelivery()
    {
        var connection = Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028";
        var database = "aep_outbox_" + Guid.NewGuid().ToString("N");
        var store = new MongoPlatformStore(connection, database); await store.InitializeAsync(default);
        var run = await store.CreateRunAsync(new() { ProjectId = "p", IdempotencyKey = "outbox" }, default);
        var claimed = (await store.ClaimAsync("first-worker", TimeSpan.FromMinutes(1), default))!;
        Assert.True(await store.SaveRunAsync(claimed.Transition(RunStage.Analyzing), claimed.Version, "first-worker", default));
        var recovered = new MongoPlatformStore(connection, database);
        var events = await recovered.FlushOutboxAsync(default); Assert.Contains(events, x => x.Name == "StageChanged" && x.Content.Contains("Analyzing"));
        Assert.Empty(await recovered.FlushOutboxAsync(default));
        var persisted = await recovered.RecordsAsync("event", "p", run.Id, null, 0, 0, 100, default); Assert.Equal(events.Count, persisted.Count); Assert.All(persisted, x => Assert.True(x.Sequence > 0));
    }
    [Fact] public async Task RemoteSandboxFailsClosed() { var sandbox = new RemoteExecutionSandbox(new HttpClient(), null, null); Assert.False(sandbox.Configured); await Assert.ThrowsAsync<ConfigurationException>(() => sandbox.ExecuteAsync(new("op", "run", new("owner", "repo", 1), "RunTests", JsonSerializer.SerializeToElement(new { })), default)); }
    [Theory]
    [InlineData("../secrets")]
    [InlineData("/etc/passwd")]
    [InlineData(".git/config")]
    [InlineData("nested/../../escape")]
    [InlineData(".env.production")]
    [InlineData("secrets/app.pem")]
    public async Task ToolRejectsTraversal(string path)
    {
        var tool = new EngineeringTool("ReadFile", new DeterministicSandbox(), new DeterministicSourceControl(), factory.Services.GetRequiredService<IPlatformStore>());
        var definition = new AgentDefinition("a", AgentRole.BackendEngineer, "a", "", "test", "", [], ["ReadFile"]);
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(new(new Project(), new ExecutionRun(), "", definition), JsonSerializer.SerializeToElement(new { path }), "op", default));
    }
    private static async Task<ExecutionRun> WaitAsync(HttpClient client, string id, RunStage expected)
    {
        for (var i = 0; i < 200; i++) { var run = (await client.GetFromJsonAsync<ExecutionRun>("/api/v1/runs/" + id, Json))!; if (run.CurrentStage == expected) return run; if (run.CurrentStage == RunStage.Failed) throw new Exception(run.FailureReason); await Task.Delay(100); }
        throw new TimeoutException("Execution did not reach " + expected);
    }
}
