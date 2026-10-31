using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Application.RuntimeV2;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using AIEngineeringPlatform.Agents;
using System.Text.Json;
using MongoDB.Driver;
using Xunit;

namespace AIEngineeringPlatform.Integration.Tests;

public sealed class IndependentAgentDispatchTests
{
    private static readonly string Connection = Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028";
    private sealed class Provider(Func<AgentContext, CancellationToken, Task<ModelExecutionResult>>? invoke = null) : ILanguageModelProvider
    {
        public int Calls;
        public Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
        { Interlocked.Increment(ref Calls); return invoke?.Invoke(context, ct) ?? Task.FromResult(Result()); }
    }
    private static ModelExecutionResult Result() => new(new("Source is a proposal until gateway execution and verification", "CONTINUE", [], [], [], [], "Use actual test evidence"), "resp_dispatch_fixture", 10, 10, .01m);
    private sealed record Fixture(string Database, MongoPlatformStore Store, MongoAgentDispatchStore Queue, DurableAgentDispatcher Dispatcher, AgentContext Context, IRuntimeContextFrames Frames, IRuntimeInvocationStore Invocations)
    {
        public IndependentAgentTaskProcessor Processor(ILanguageModelProvider provider) => new(Store, Queue, new OpenAIResponsesAgentRuntime(provider, Frames, Invocations));
    }
    private static async Task<Fixture> Create(AgentRole role = AgentRole.BackendEngineer)
    {
        var db = "aep_dispatch_test_" + Guid.NewGuid().ToString("N");
        var store = new MongoPlatformStore(Connection, db); await store.InitializeAsync(default);
        var project = new Project { Name = "Independent worker fixture" }; await store.SaveProjectAsync(project, null, default);
        var run = await store.CreateRunAsync(new() { ProjectId = project.Id, ProjectSnapshot = project, AgentRuntimeVersion = 2, CurrentStage = role == AgentRole.BackendEngineer ? RunStage.Implementing : RunStage.GeneratingTests }, default);
        run = await store.ClaimAsync("orchestrator-fixture", TimeSpan.FromMinutes(2), default) ?? throw new Exception("Fixture could not claim run");
        var definition = new AgentDefinition(role.ToString(), role, role.ToString(), "Bounded task", "fixture-model", "Use authorized tools", [], ["ReadRepository", "WriteFile"], InputCostPerMillion: 1, OutputCostPerMillion: 2);
        await store.SaveAgentAsync(definition, default);
        var frames = new PersistedRuntimeContextFrames(store); var queue = new MongoAgentDispatchStore(Connection, db);
        return new(db, store, queue, new(store, queue, frames, new(store)), new(project, run, "Current objective, protected implementation instructions and actual source evidence", definition, "stable-task"), frames, new MongoRuntimeInvocationStore(Connection, db));
    }
    private static async Task<AgentDispatch> WaitScheduled(Fixture fixture)
    {
        var id = PersistedRuntimeContextFrames.Hash(fixture.Context.Run.Id + "/stable-task/0");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var saved = await fixture.Queue.GetAsync(id, timeout.Token);
            if (saved != null) return saved;
            await Task.Delay(20, timeout.Token);
        }
    }
    [Theory]
    [InlineData(AgentRole.BackendEngineer)]
    [InlineData(AgentRole.TestEngineer)]
    public async Task SeparateWorkerCompletesBoundedInvocationAndRecoveryDoesNotRecallProvider(AgentRole role)
    {
        var f = await Create(role); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pending = f.Dispatcher.ExecuteAsync(f.Context, timeout.Token); var task = await WaitScheduled(f);
        var provider = new Provider();
        // A fresh store/runtime simulates another worker process reading the durable task.
        var queue = new MongoAgentDispatchStore(Connection, f.Database);
        var processor = new IndependentAgentTaskProcessor(f.Store, queue, new OpenAIResponsesAgentRuntime(provider, f.Frames, f.Invocations));
        Assert.True(await processor.ProcessOneAsync(role, "separate-worker", timeout.Token));
        Assert.Equal("resp_dispatch_fixture", (await pending).ResponseId); Assert.Equal(1, provider.Calls);
        var recovered = new DurableAgentDispatcher(f.Store, queue, f.Frames, new(f.Store));
        var response = await recovered.ExecuteAsync(f.Context with { Context = "Rebuilt context must not replace frozen input" }, timeout.Token);
        Assert.Equal("resp_dispatch_fixture", response.ResponseId); Assert.Equal(1, provider.Calls);
        var saved = await queue.GetAsync(task.Id, default); Assert.Equal("Completed", saved!.Status);
        Assert.Equal(task.Request.Context.Context, saved.Request.Context.Context);
        Assert.Empty(await f.Store.RecordsAsync("tool", f.Context.Project.Id, f.Context.Run.Id, null, 0, 0, 10, default));
        Assert.Empty(await f.Store.RecordsAsync("task", f.Context.Project.Id, f.Context.Run.Id, null, 0, 0, 10, default));
        // Worker completion itself creates neither source/test evidence nor legacy feature completion.
        Assert.Equal("Unverified", (await f.Store.GetRecordAsync("agentMessageV2", task.Id, default))!.Status);
    }
    [Fact]
    public async Task CompetingWorkersOnlyOneClaimsAndNoOtherRoleMayConsumeTask()
    {
        var f = await Create(); using var cancel = new CancellationTokenSource();
        var pending = f.Dispatcher.ExecuteAsync(f.Context, cancel.Token); await WaitScheduled(f);
        Assert.Null(await f.Queue.ClaimAsync(AgentRole.TestEngineer, "wrong-role", default));
        var other = new MongoAgentDispatchStore(Connection, f.Database);
        var claims = await Task.WhenAll(f.Queue.ClaimAsync(AgentRole.BackendEngineer, "one", default), other.ClaimAsync(AgentRole.BackendEngineer, "two", default));
        Assert.Single(claims, x => x != null);
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
    [Fact]
    public async Task WorkerCrashPreservesRunningClaimAndNeverAutomaticallyRedispatches()
    {
        var f = await Create(); using var cancel = new CancellationTokenSource();
        var pending = f.Dispatcher.ExecuteAsync(f.Context, cancel.Token); var scheduled = await WaitScheduled(f);
        Assert.NotNull(await f.Queue.ClaimAsync(AgentRole.BackendEngineer, "crashed-worker", default));
        Assert.Null(await new MongoAgentDispatchStore(Connection, f.Database).ClaimAsync(AgentRole.BackendEngineer, "replacement", default));
        Assert.Equal("Running", (await f.Queue.GetAsync(scheduled.Id, default))!.Status);
        await Assert.ThrowsAsync<RuntimeReconciliationException>(() => f.Queue.FinishAsync(scheduled.Id, "replacement", Result(), null, default));
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
    [Fact]
    public async Task PausingDuringModelRequestCancelsAndPreservesUnknownOutcomeWithoutRetry()
    {
        var f = await Create(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider(async (_, ct) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return Result(); });
        var pending = f.Dispatcher.ExecuteAsync(f.Context, timeout.Token); var task = await WaitScheduled(f);
        var process = f.Processor(provider).ProcessOneAsync(AgentRole.BackendEngineer, "worker", timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        var run = (await f.Store.GetRunAsync(f.Context.Run.Id, default))!;
        Assert.True(await f.Store.SaveRunAsync(run.Transition(RunStage.Paused, "Owner pause"), run.Version, run.LeaseOwner, default));
        Assert.True(await process); await Assert.ThrowsAsync<TaskReconciliationException>(() => pending);
        Assert.Equal(1, provider.Calls); Assert.Equal("OutcomeUnknown", (await f.Queue.GetAsync(task.Id, default))!.Status);
        Assert.Equal("OutcomeUnknown", (await f.Invocations.GetAsync(task.Id, default)).Status);
        Assert.False(await f.Processor(provider).ProcessOneAsync(AgentRole.BackendEngineer, "restart", default));
    }
    [Fact]
    public async Task RevokedRoleIsNotDispatchedAndLegacyRunKeepsDirectCompatiblePath()
    {
        var f = await Create(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pending = f.Dispatcher.ExecuteAsync(f.Context, timeout.Token); await WaitScheduled(f);
        await f.Store.SaveAgentAsync(f.Context.Definition with { Enabled = false }, default);
        var provider = new Provider(); Assert.True(await f.Processor(provider).ProcessOneAsync(AgentRole.BackendEngineer, "worker", timeout.Token));
        await Assert.ThrowsAsync<TaskReconciliationException>(() => pending); Assert.Equal(0, provider.Calls);
        var agent = new DispatchedEngineeringAgent(AgentRole.BackendEngineer, provider, f.Dispatcher);
        await agent.ExecuteAsync(f.Context with { Run = f.Context.Run with { AgentRuntimeVersion = 1 } }, default);
        Assert.Equal(1, provider.Calls);
    }
    [Fact]
    public async Task NewRunOptInIsPersistedAndDoesNotMigrateExistingRuns()
    {
        var f = await Create(); var project = new Project { Name = "New-run opt-in" }; await f.Store.SaveProjectAsync(project, null, default);
        var publisher = NSubstitute.Substitute.For<IExecutionPublisher>();
        var legacy = await new PlatformService(f.Store, publisher).StartAsync(project.Id, "legacy", default);
        Assert.Equal(1, legacy.AgentRuntimeVersion);
        Assert.True(await f.Store.SaveRunAsync(legacy.Transition(RunStage.Cancelled), legacy.Version, null, default));
        var modern = await new PlatformService(f.Store, publisher, runtimeOptions: new() { EnableNewRuns = true }).StartAsync(project.Id, "modern", default);
        Assert.Equal(2, modern.AgentRuntimeVersion);
        Assert.Equal(1, (await f.Store.GetRunAsync(legacy.Id, default))!.AgentRuntimeVersion);
    }
    [Fact]
    public async Task ExpiredClaimBecomesUnknownAndRetainsOriginalInputWithoutRequeue()
    {
        var f = await Create(); using var cancel = new CancellationTokenSource();
        var pending = f.Dispatcher.ExecuteAsync(f.Context, cancel.Token); var task = await WaitScheduled(f);
        var claimed = (await f.Queue.ClaimAsync(AgentRole.BackendEngineer, "lost-worker", default))!;
        var expired = claimed with { CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-6), Deadline = DateTimeOffset.UtcNow.AddSeconds(-1) };
        var collection = new MongoDB.Driver.MongoClient(Connection).GetDatabase(f.Database).GetCollection<MongoDB.Bson.BsonDocument>("agent_dispatch_v2");
        await collection.UpdateOneAsync(MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("_id", task.Id), MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Update.Set("Deadline", expired.Deadline.UtcDateTime).Set("Data", JsonSerializer.Serialize(expired)));
        var recovered = (await f.Queue.GetAsync(task.Id, default))!;
        Assert.Equal("OutcomeUnknown", recovered.Status); Assert.Equal("lost-worker", recovered.Worker);
        Assert.Equal(JsonSerializer.Serialize(task.Request), JsonSerializer.Serialize(recovered.Request));
        Assert.Null(await f.Queue.ClaimAsync(AgentRole.BackendEngineer, "new-worker", default));
        await Assert.ThrowsAsync<TaskReconciliationException>(() => pending);
    }

    [Fact]
    public async Task DurableRuntimeCompletionRepairsCrashGapWithoutCallingProviderAgain()
    {
        var f = await Create(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pending = f.Dispatcher.ExecuteAsync(f.Context, timeout.Token); var task = await WaitScheduled(f);
        var claimed = (await f.Queue.ClaimAsync(AgentRole.BackendEngineer, "worker-before-crash", default))!;
        var provider = new Provider();
        await new OpenAIResponsesAgentRuntime(provider, f.Frames, f.Invocations).ExecuteAsync(claimed.Request, timeout.Token);
        // Simulate a process crash after the runtime commit and before queue completion.
        Assert.Equal("resp_dispatch_fixture", (await pending).ResponseId);
        var saved = (await new MongoAgentDispatchStore(Connection, f.Database).GetAsync(task.Id, default))!;
        Assert.Equal("Completed", saved.Status); Assert.Equal("worker-before-crash", saved.Worker);
        Assert.Equal(1, provider.Calls);
        Assert.False(await f.Processor(provider).ProcessOneAsync(AgentRole.BackendEngineer, "replacement", default));
        Assert.Equal(1, provider.Calls);
    }

}
