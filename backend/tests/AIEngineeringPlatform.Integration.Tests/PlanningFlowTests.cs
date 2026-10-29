using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
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
