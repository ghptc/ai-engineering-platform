using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AIEngineeringPlatform.Integration.Tests;

public sealed partial class RuntimeFlowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedInspectionGetsFeedbackThenBlocksAndCannotEscapeThroughResume(bool runtimeV2)
    {
        await using var root = new PlatformFactory();
        var model = new InspectForeverModel();
        var sandbox = new DeterministicSandbox();
        await using var factory = root.WithWebHostBuilder(builder => { builder.UseSetting("AGENT_RUNTIME_V2_NEW_RUNS", runtimeV2.ToString()); builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(model);
            services.RemoveAll<IExecutionSandbox>(); services.AddSingleton<IExecutionSandbox>(sandbox);
        }); });
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var run = await Start(client, store, ExecutionPresets.StandardDevelopment);
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.Paused, run.CurrentStage);
        Assert.Equal(RunStage.Implementing, run.ResumeStage);
        Assert.Equal(ExecutionOutcome.Blocked, run.Outcome);
        Assert.True(model.SawFeedback);
        Assert.Equal(InspectionProgress.StopAfter + 1, model.Calls);
        Assert.Single(sandbox.Operations, x => x == "ReadFile");
        Assert.Single(sandbox.Operations, x => x == "ReadRepository");
        Assert.DoesNotContain(sandbox.Operations, x => x is "WriteFile" or "ApplyPatch" or "RunTests" or "CreateCommit" or "PushBranch");
        var checkpoints = await store.RecordsAsync("task", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        var policy = run.Policy;
        var calls = model.Calls;
        (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/resume", new { })).EnsureSuccessStatusCode();
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(calls, model.Calls);
        Assert.Equal(policy, run.Policy);
        var after = await store.RecordsAsync("task", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.All(checkpoints, x => Assert.Single(after, y => y.Id == x.Id && y.Content == x.Content));
        var events = await store.RecordsAsync("event", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.Contains(events, x => x.Name == "InspectionProgressFeedback");
        Assert.Contains(events, x => x.Name == "InspectionProgressBlocked");
    }

    private sealed class InspectForeverModel : ILanguageModelProvider
    {
        public int Calls;
        public bool SawFeedback;
        public Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            SawFeedback |= context.Context.Contains(InspectionProgress.Feedback, StringComparison.Ordinal);
            ToolCall[] calls = [new("ReconcileRepository", JsonSerializer.SerializeToElement(new { })), new("ReadRepository", JsonSerializer.SerializeToElement(new { })), new("ReadProjectMemory", JsonSerializer.SerializeToElement(new { kind = "artifact" })), new("ReadFile", JsonSerializer.SerializeToElement(new { path = "backend/Feature.cs" }))];
            return Task.FromResult(new ModelExecutionResult(new("Inspect before implementing", "CONTINUE", [], [], [], calls, ""), "fixture-response-" + Calls, 100, 50, 0.001m));
        }
    }
}
