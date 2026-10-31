using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Application.RuntimeV2;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace AIEngineeringPlatform.Integration.Tests;
public sealed class AgentRuntimeV2Tests
{
    private const string Revision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private sealed class Provider(Func<AgentContext, CancellationToken, Task<ModelExecutionResult>>? execute = null) : ILanguageModelProvider
    {
        public int Calls;
        public AgentContext? Received;
        public Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls); Received = context;
            return execute?.Invoke(context, ct) ?? Task.FromResult(Result());
        }
    }
    private static ModelExecutionResult Result(ToolCall[]? tools = null) => new(new("Bounded model proposal, not certified implementation", "CONTINUE", [], [], [], tools ?? [], ""), "resp_runtime_fixture", 10, 10, .01m);
    private static async Task<AgentRuntimeRequest> Request(PlatformFactory factory)
    {
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var project = new Project { Name = "Runtime V2 isolated fixture" };
        await store.SaveProjectAsync(project, null, default);
        var run = await store.CreateRunAsync(new() { ProjectId = project.Id, ProjectSnapshot = project, CurrentStage = RunStage.Paused }, default);
        var task = Guid.NewGuid().ToString("N");
        var frame = await factory.Services.GetRequiredService<IRuntimeContextFrames>().SaveAsync(project.Id, run.Id, task, "{\"objective\":\"Implement one bounded task\"}", default);
        var definition = new AgentDefinition("BackendEngineer", AgentRole.BackendEngineer, "Backend Engineer", "Bounded implementation", "test-only-model", "Use authorized tools only.", [], ["ReadRepository", "WriteFile"], InputCostPerMillion: 1, OutputCostPerMillion: 2);
        var payload = new EngineeringTaskPayload(AgentRole.BackendEngineer, EngineeringTaskPurpose.Implementation, true, ["ReadRepository"], new(2, 2, 40000, 1000, 30, 1), [], frame.Id);
        var message = new EngineeringMessage(1, EngineeringMessageKind.EngineeringTask, Guid.NewGuid().ToString("N"), project.Id, run.Id, task, "Orchestrator", "BackendEngineer", null, "Implement the current bounded task", ["Use real provider evidence"], [], [], new("Implementing", 0, task), JsonSerializer.SerializeToElement(payload, EngineeringMessageContract.Json));
        return new(message, new(project, run, frame.Content, definition), frame.Id, frame.Sha256);
    }
    [Fact] public async Task CompletedInvocationAndContextSurviveRuntimeRecreationWithoutAnotherProviderCall()
    {
        await using var factory = new PlatformFactory(); var request = await Request(factory);
        var frames = factory.Services.GetRequiredService<IRuntimeContextFrames>(); var checkpoints = factory.Services.GetRequiredService<IRuntimeInvocationStore>();
        var firstProvider = new Provider(); var first = new OpenAIResponsesAgentRuntime(firstProvider, frames, checkpoints);
        var result = await first.ExecuteAsync(request, default);
        Assert.Equal(1, firstProvider.Calls);
        Assert.Equal(new[] { "ReadRepository" }, firstProvider.Received!.Definition.AllowedTools);
        Assert.Equal(2, firstProvider.Received.RuntimeLimits!.MaxModelRequests);
        var replacementProvider = new Provider();
        var replacementStore = new MongoRuntimeInvocationStore(Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028", factory.Database);
        var cached = await new OpenAIResponsesAgentRuntime(replacementProvider, frames, replacementStore).ExecuteAsync(request, default);
        Assert.Equal(result.Response.ResponseId, cached.Response.ResponseId);
        Assert.Equal(0, replacementProvider.Calls);
        var exhausted = request with { Context = request.Context with { Run = request.Context.Run with { Cost = request.Context.Run.Policy.MaxBudgetUsd } } };
        await new OpenAIResponsesAgentRuntime(replacementProvider, frames, replacementStore).ExecuteAsync(exhausted, default);
        Assert.Equal(0, replacementProvider.Calls);
        Assert.Equal("Completed", (await replacementStore.GetAsync(request.Task.MessageId, default)).Status);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => frames.GetAsync(request.ContextFrameId, "other-project", request.Task.RunId, default));
    }
    [Fact] public async Task ConcurrentDuplicateDispatchMakesOnlyOneModelRequest()
    {
        await using var factory = new PlatformFactory(); var request = await Request(factory);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider(async (_, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return Result(); });
        var runtime = new OpenAIResponsesAgentRuntime(provider, factory.Services.GetRequiredService<IRuntimeContextFrames>(), factory.Services.GetRequiredService<IRuntimeInvocationStore>());
        var one = runtime.ExecuteAsync(request, default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var two = runtime.ExecuteAsync(request, default); release.TrySetResult();
        var results = await Task.WhenAll(one, two);
        Assert.Equal(1, provider.Calls); Assert.Equal(results[0].Response.ResponseId, results[1].Response.ResponseId);
    }
    [Fact] public async Task UnauthorizedToolsCannotBeGrantedOrExecutedByAnEnvelope()
    {
        await using var factory = new PlatformFactory(); var request = await Request(factory); var provider = new Provider();
        var task = EngineeringMessageContract.Payload<EngineeringTaskPayload>(request.Task) with { AuthorizedTools = ["HostShell"] };
        request = request with { Task = request.Task with { Payload = JsonSerializer.SerializeToElement(task, EngineeringMessageContract.Json) } };
        var runtime = new OpenAIResponsesAgentRuntime(provider, factory.Services.GetRequiredService<IRuntimeContextFrames>(), factory.Services.GetRequiredService<IRuntimeInvocationStore>());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => runtime.ExecuteAsync(request, default)); Assert.Equal(0, provider.Calls);
    }
    [Fact] public async Task InterruptedInvocationIsNotAutomaticallyReissued()
    {
        await using var factory = new PlatformFactory(); var request = await Request(factory);
        var provider = new Provider((_, _) => throw new TaskCanceledException("Test-only interrupted provider"));
        var checkpoints = factory.Services.GetRequiredService<IRuntimeInvocationStore>();
        var runtime = new OpenAIResponsesAgentRuntime(provider, factory.Services.GetRequiredService<IRuntimeContextFrames>(), checkpoints);
        await Assert.ThrowsAsync<TaskCanceledException>(() => runtime.ExecuteAsync(request, default));
        Assert.Equal("OutcomeUnknown", (await checkpoints.GetAsync(request.Task.MessageId, default)).Status);
        await Assert.ThrowsAsync<RuntimeReconciliationException>(() => runtime.ExecuteAsync(request, default));
        Assert.Equal(1, provider.Calls);
    }
    [Fact] public async Task VersionedCommunicationIsDurableAndCannotChangeExecutionScope()
    {
        await using var factory = new PlatformFactory(); var request = await Request(factory);
        var messages = factory.Services.GetRequiredService<DurableEngineeringMessages>();
        await messages.PersistAsync(request.Task, "Orchestrator", new HashSet<string> { "BackendEngineer" }, default);
        await messages.PersistAsync(request.Task, "Orchestrator", new HashSet<string> { "BackendEngineer" }, default);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        Assert.Single(await store.RecordsAsync("agentMessageV2", request.Task.ProjectId, request.Task.RunId, null, 0, 0, 100, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => messages.PersistAsync(request.Task with { ProjectId = "other-project" }, "Orchestrator", new HashSet<string> { "BackendEngineer" }, default));
    }
    [Fact] public async Task TestReportMustMatchNonzeroRealReceiptCountsAndTheExactRevision()
    {
        await using var factory = new PlatformFactory(); var request = await Request(factory);
        var store = factory.Services.GetRequiredService<IPlatformStore>(); var operation = Guid.NewGuid().ToString("N");
        var result = new ToolResult(true, "Executor evidence: tests=3; exit=0; timeout=False; outputLimit=False", "local-" + operation, Revision, 0);
        await store.AppendAsync(new() { Id = operation, Kind = "tool", ProjectId = request.Task.ProjectId, RunId = request.Task.RunId, Name = "RunTests", Status = "Succeeded", Content = JsonSerializer.Serialize(result) }, default);
        var report = request.Task with { Kind = EngineeringMessageKind.TestReport, Sender = "TestEngineer", Recipient = "Orchestrator", SourceRevision = Revision, Evidence = [new(operation, operation, Revision)], Payload = JsonSerializer.SerializeToElement(new TestReportPayload("Passed", 3, []), EngineeringMessageContract.Json) };
        var resolver = factory.Services.GetRequiredService<RuntimeEvidenceResolver>();
        Assert.Equal(3, Assert.Single(await resolver.ResolveAsync(report, default)).ExecutedTests);
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(report with { SourceRevision = new string('b', 64) }, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(report with { Payload = JsonSerializer.SerializeToElement(new TestReportPayload("Passed", 999, []), EngineeringMessageContract.Json) }, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resolver.ResolveAsync(report with { RunId = "another-run" }, default));
    }
}
