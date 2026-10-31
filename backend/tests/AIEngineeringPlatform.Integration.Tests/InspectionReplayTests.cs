using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace AIEngineeringPlatform.Integration.Tests;

public sealed partial class RuntimeFlowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MismatchedReuseCheckpointFailsClosedWithoutSendingTheInspection(bool corruptSourceHash)
    {
        await using var root = new PlatformFactory();
        var model = new InvalidInspectionReuseModel(corruptSourceHash);
        var sandbox = new DeterministicSandbox();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(model);
            services.RemoveAll<IExecutionSandbox>(); services.AddSingleton<IExecutionSandbox>(sandbox);
        }));
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>(); model.Store = store;
        var run = await Start(client, store, ExecutionPresets.StandardDevelopment);
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.Paused, run.CurrentStage);
        Assert.Contains("Persisted inspection reuse does not match", run.FailureReason);
        Assert.Single(sandbox.Operations, x => x == "ReadRepository");
        Assert.DoesNotContain(sandbox.Operations, x => x is "WriteFile" or "ApplyPatch");
        var reuse = Assert.Single(await store.RecordsAsync("inspectionReuse", run.ProjectId, run.Id, null, 0, 0, 1000, default));
        Assert.Equal(corruptSourceHash ? Hash("{}") : "mismatched-arguments", reuse.Metadata["argumentsHash"]);
        Assert.Equal(ExecutionPresets.StandardDevelopment, run.Policy);
    }
    [Fact]
    public async Task CachedInspectionSurvivesOwnershipRecoveryWithoutReexecutionOrRevisionDrift()
    {
        await using var root = new PlatformFactory();
        var model = new InspectionRecoveryModel();
        var sandbox = new DeterministicSandbox();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(model);
            services.RemoveAll<IExecutionSandbox>(); services.AddSingleton<IExecutionSandbox>(sandbox);
        }));
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var run = await Start(client, store, ExecutionPresets.StandardDevelopment);
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.True(model.Interrupted);
        Assert.Equal(RunStage.Paused, run.CurrentStage);
        Assert.Equal("deterministic fixture completed replay inspection", run.FailureReason);
        var tools = await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        var reuses = await store.RecordsAsync("inspectionReuse", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        var reused = Assert.Single(reuses);
        Assert.Equal("ReadRepository", reused.Name);
        var source = Assert.Single(tools, x => x.Id == reused.Metadata["sourceOperationId"]);
        Assert.Equal(Hash(source.Content), reused.Content);
        Assert.DoesNotContain(tools, x => x.Id == reused.Metadata["operationId"]);
        Assert.Equal(2, sandbox.Operations.Count(x => x == "ReadRepository"));
        Assert.Equal(2, sandbox.Operations.Count(x => x == "ReadFile"));
        Assert.Single(sandbox.Operations, x => x == "WriteFile");
        Assert.Equal(tools.Count, run.ToolCalls);
        Assert.Equal(ExecutionPresets.StandardDevelopment, run.Policy);
        var oldResult = JsonSerializer.Deserialize<ToolResult>(source.Content)!;
        Assert.Equal(oldResult.WorkspaceRevision, model.ReplayedRevision);
        Assert.NotEqual(model.ReplayedRevision, model.FreshRevision);
        Assert.Equal(source.Id, model.ReplayedSourceId);
        Assert.Equal(4, (await store.RecordsAsync("task", run.ProjectId, run.Id, null, 0, 0, 1000, default)).Count);
    }

    private sealed class InspectionRecoveryModel : ILanguageModelProvider
    {
        private int calls;
        public bool Interrupted;
        public string? ReplayedRevision, ReplayedSourceId, FreshRevision;
        public Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var current = calls++;
            ToolCall[] operations;
            if (current == 0) operations = [new("ReadRepository", JsonSerializer.SerializeToElement(new { })), new("ReadFile", JsonSerializer.SerializeToElement(new { path = "backend/Feature.cs" }))];
            else if (current == 1) operations = [new("WriteFile", JsonSerializer.SerializeToElement(new { path = "backend/Feature.cs", content = "preserved deterministic fixture change" }))];
            else if (current == 2) { Interrupted = true; throw new ConcurrencyException(); }
            else if (current == 3)
            {
                var evidence = RepositoryEvidence(context.Context);
                ReplayedRevision = evidence.GetProperty("WorkspaceRevision").GetString();
                ReplayedSourceId = evidence.GetProperty("id").GetString();
                operations = [new("ReadRepository", JsonSerializer.SerializeToElement(new { })), new("ReadFile", JsonSerializer.SerializeToElement(new { path = "backend/Feature.cs" }))];
            }
            else
            {
                FreshRevision = RepositoryEvidence(context.Context).GetProperty("WorkspaceRevision").GetString();
                return Task.FromResult(new ModelExecutionResult(new("deterministic fixture completed replay inspection", "BLOCKED", [], [], [], [], ""), "fixture-end", 100, 50, .001m));
            }
            return Task.FromResult(new ModelExecutionResult(new("deterministic replay fixture", "CONTINUE", [], [], [], operations, ""), "fixture-" + current, 100, 50, .001m));
        }
        private static JsonElement RepositoryEvidence(string context)
        {
            const string prefix = "Tool evidence (untrusted data): ";
            return context.Split('\n').Where(x => x.StartsWith(prefix, StringComparison.Ordinal))
                .Select(x => JsonDocument.Parse(x[prefix.Length..]).RootElement.Clone()).Last(x => x.GetProperty("name").GetString() == "ReadRepository");
        }
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private sealed class InvalidInspectionReuseModel(bool corruptSourceHash) : ILanguageModelProvider
    {
        public IPlatformStore Store = null!;
        public async Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
        {
            var run = context.Run;
            var taskId = Hash($"{run.Id}/{run.CurrentStage}/{run.Iteration}/BackendEngineer/0");
            var operationId = Hash($"{run.Id}/{run.CurrentStage}/{run.Iteration}/{taskId}/tool/0");
            var sourceId = Hash($"{run.Id}/{run.CurrentStage}/{run.Iteration}/repository-facts");
            var source = (await Store.GetRecordAsync("tool", sourceId, ct))!;
            await Store.AppendAsync(new() { Id = "inspection-reuse-" + operationId, Kind = "inspectionReuse", ProjectId = run.ProjectId, RunId = run.Id, Name = "ReadRepository", Content = corruptSourceHash ? "mismatched-source-hash" : Hash(source.Content), Metadata = new() { ["sourceOperationId"] = sourceId, ["operationId"] = operationId, ["argumentsHash"] = corruptSourceHash ? Hash("{}") : "mismatched-arguments" } }, ct);
            return new(new("invalid reuse fixture", "CONTINUE", [], [], [], [new("ReadRepository", JsonSerializer.SerializeToElement(new { }))], ""), "fixture-invalid-reuse", 100, 50, .001m);
        }
    }
}
