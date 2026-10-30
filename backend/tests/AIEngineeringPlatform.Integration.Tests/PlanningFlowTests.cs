using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using System.Net.Http.Json;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;

public sealed partial class RuntimeFlowTests
{
    [Theory]
    [InlineData("safe", RunStage.AwaitingHumanApproval, true)]
    [InlineData("noProfiles", RunStage.Paused, true)]
    [InlineData("unsupported", RunStage.Paused, true)]
    [InlineData("infrastructure", RunStage.Paused, true)]
    [InlineData("planning", RunStage.Paused, false)]
    public async Task ReadmeOnlyPlanningRoutesByBlockerStageAndNeverApprovesUntestedChanges(string scenario, RunStage expected, bool architectRuns)
    {
        await using var root = new PlatformFactory();
        var sandbox = new PlanningSandbox(scenario == "unsupported", scenario == "noProfiles");
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(new PlanningModel(scenario));
            services.RemoveAll<IExecutionSandbox>(); services.AddSingleton<IExecutionSandbox>(sandbox);
        }));
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var run = await Start(client, store, ExecutionPresets.StandardDevelopment, planning: true);
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(expected, run.CurrentStage);
        var tasks = await store.RecordsAsync("task", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.Equal(architectRuns, tasks.Any(x => x.Name == "Architect"));
        if (architectRuns) Assert.Contains(run.AgentTransitions, x => x.SourceAgent == AgentRole.BusinessAnalyst && x.TargetAgent == AgentRole.Architect);
        if (scenario is "planning" or "infrastructure" or "noProfiles") Assert.DoesNotContain("WriteFile", sandbox.Operations);
        if (scenario != "safe")
        {
            Assert.DoesNotContain(tasks, x => x.Name == "QAEngineer");
            Assert.DoesNotContain("RunTests", sandbox.Operations);
            Assert.Equal(ExecutionOutcome.Blocked, run.Outcome);
        }
        Assert.DoesNotContain("PushBranch", sandbox.Operations);
    }

    [Fact]
    public async Task ArchitectCheckpointRetryPreservesCompletedAnalysisAndValidToolEvidence()
    {
        await using var root = new PlatformFactory();
        var model = new IncompleteArchitect();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(model);
        }));
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var run = await Start(client, store, ExecutionPresets.StandardDevelopment, planning: true);
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.Failed, run.CurrentStage);
        Assert.Equal(RunStage.Planning, run.ResumeStage);
        var analysisCalls = model.AnalysisCalls;
        var before = await store.RecordsAsync("artifact", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.NotEmpty(before);
        var tools = await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.NotEmpty(tools);
        (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/retry", new { })).EnsureSuccessStatusCode();
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.AwaitingHumanApproval, run.CurrentStage);
        Assert.Equal(analysisCalls, model.AnalysisCalls);
        var artifacts = await store.RecordsAsync("artifact", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        var after = await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.All(before, item => Assert.Single(artifacts, x => x.Id == item.Id && x.Content == item.Content));
        Assert.All(tools, item => Assert.Single(after, x => x.Id == item.Id && x.Content == item.Content));
        Assert.DoesNotContain(after, x => x.Name is "CreateCommit" or "PushBranch" or "CreatePullRequest");
    }
    private sealed class IncompleteArchitect : ILanguageModelProvider
    {
        private int architectCalls;
        public int AnalysisCalls;
        private readonly DeterministicModel inner = new();
        public async Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
        {
            if (context.Definition.Role == AgentRole.BusinessAnalyst) AnalysisCalls++;
            if (context.Definition.Role == AgentRole.Architect && ++architectCalls == 1)
            {
                var result = await inner.ExecuteAsync(context, ct);
                return result with { Output = result.Output with { ToolCalls = [new("ReadRepository", JsonSerializer.SerializeToElement(new { }))] } };
            }
            if (context.Definition.Role == AgentRole.Architect && architectCalls == 2)
            {
                throw new ProviderException(new(context.Definition.Model, "resp_incomplete", "req_incomplete", "incomplete", "max_output_tokens", ProviderErrorCategory.IncompleteResponse, 100, 4000, 1, MaxOutputTokens: 4000));
            }
            return await inner.ExecuteAsync(context, ct);
        }
    }

    private sealed class PlanningModel(string scenario) : ILanguageModelProvider
    {
        private readonly DeterministicModel inner = new();
        public async Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
        {
            var result = await inner.ExecuteAsync(context, ct);
            if (context.Definition.Role is not (AgentRole.BusinessAnalyst or AgentRole.Architect)) return result;
            if (!context.Context.Contains("README.md", StringComparison.Ordinal))
                return result with { Output = result.Output with { ToolCalls = [new("ReadRepository", JsonSerializer.SerializeToElement(new { }))] } };
            PlanningBlocker[] blockers = scenario == "infrastructure" ? [new("Implementation", "Required offline dependency unavailable", "fixture-capability-evidence")]
                : scenario == "planning" ? [new("Planning", "Authorized repository cannot be inspected", "fixture-denied-evidence")] : [];
            return result with { Output = result.Output with
            {
                Decision = blockers.Length > 0 ? "BLOCKED" : "CONTINUE",
                Planning = new(["SDK/cache not yet verified"], ["Bootstrap source and tests using approved profiles"], blockers,
                    "WriteFile backend and frontend manifests, lockfiles, source and tests", "Verify offline SDK/package cache through administrator profiles", "Run required profiles and bind evidence to workspace revision")
            } };
        }
    }

    private sealed class PlanningSandbox(bool unsupported, bool noProfiles) : IExecutionSandbox
    {
        private readonly DeterministicSandbox inner = new();
        public List<string> Operations => inner.Operations;
        public bool Configured => true;
        public async Task<SandboxResponse> ExecuteAsync(SandboxRequest request, CancellationToken ct)
        {
            var response = await inner.ExecuteAsync(request, ct);
            if (request.Operation != "ReadRepository") return response;
            var written = Operations.Contains("WriteFile");
            return response with { Output = JsonSerializer.Serialize(new { Files = written ? new[] { "README.md", "backend/App.csproj" } : ["README.md"], TestProfiles = written && !unsupported ? new[] { "node" } : [], HasSourceChanges = written, Capabilities = new { Profiles = noProfiles ? Array.Empty<object>() : new object[] { new { Name = "node", Purpose = "Test" } } } }) };
        }
    }
}
