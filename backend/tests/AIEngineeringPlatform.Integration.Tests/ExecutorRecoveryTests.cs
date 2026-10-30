using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;

public sealed partial class RuntimeFlowTests
{
    [Fact]
    public async Task ExecutorFailurePausesWithDurableActionableEvidenceAndNoStageRetry()
    {
        await using var root = new PlatformFactory();
        var sandbox = new RestrictedExecutor();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<IExecutionSandbox>(); services.AddSingleton<IExecutionSandbox>(sandbox);
        }));
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var run = await Start(client, store, ExecutionPresets.StandardDevelopment);
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.Paused, run.CurrentStage);
        Assert.Equal("ExecutorProfileRestriction", run.FailureCategory);
        Assert.Contains("administrator", run.FailureReason);
        Assert.Equal(0, run.RetryCount); Assert.Equal(1, sandbox.Failures);
        var failures = await store.RecordsAsync("event", run.ProjectId, run.Id, null, 0, 0, 100, default);
        var failure = Assert.Single(failures, x => x.Name == "ExecutorOperationBlocked");
        Assert.Equal("422", failure.Metadata["httpStatus"]);
        Assert.Contains(failure.Metadata["operationId"], run.FailureReason);
        var writes = await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 100, default);
        Assert.Contains(writes, x => x.Name == "WriteFile" && x.Status == "Succeeded");
    }
    [Fact]
    public async Task MissingReadIsPersistedChargedAndReturnedToModelWithoutReplayingWrites()
    {
        await using var root = new PlatformFactory();
        var model = new CorrectReadModel();
        var sandbox = new MissingReadSandbox();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<IExecutionSandbox>(); services.AddSingleton<IExecutionSandbox>(sandbox);
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(model);
        }));
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var run = await Start(client, store, ExecutionPresets.StandardDevelopment);
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.AwaitingHumanApproval, run.CurrentStage);
        Assert.True(model.SawFeedback);
        var records = await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        var failed = Assert.Single(records, x => x.Name == "ReadFile" && x.Status == "Failed");
        Assert.Contains("ListFiles", failed.Content);
        Assert.Single(records, x => x.Name == "ReadFile" && x.Status == "Succeeded");
        Assert.Equal(records.Count, run.ToolCalls);
        Assert.Equal(1, run.Usage.Single(x => x.Agent == AgentRole.BackendEngineer).FailedToolInvocations);
        Assert.Equal(3, sandbox.Inner.Operations.Count(x => x == "WriteFile"));
        Assert.Equal(1, sandbox.Rejections);
        Assert.Equal(0, run.RetryCount);
    }
    private sealed class MissingReadSandbox : IExecutionSandbox
    {
        public DeterministicSandbox Inner { get; } = new();
        public int Rejections;
        public bool Configured => true;
        public Task<SandboxResponse> ExecuteAsync(SandboxRequest request, CancellationToken ct)
        {
            if (request.Operation == "ReadFile" && request.Arguments.GetProperty("path").GetString() == "missing.cs")
            {
                Rejections++;
                throw new ExecutorFailureException(ExecutorFailureCategory.InvalidArguments, request.OperationId, request.Operation, 400, false);
            }
            return Inner.ExecuteAsync(request, ct);
        }
    }
    private sealed class CorrectReadModel : ILanguageModelProvider
    {
        private readonly DeterministicModel inner = new();
        private int turn;
        public bool SawFeedback;
        public async Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
        {
            var result = await inner.ExecuteAsync(context, ct);
            if (context.Definition.Role != AgentRole.BackendEngineer || !context.Context.Contains("written backend/Feature.cs")) return result;
            ToolCall[]? calls = null;
            if (turn++ == 0) calls = [new("ReadFile", JsonSerializer.SerializeToElement(new { path = "missing.cs" }))];
            else if (turn == 2)
            {
                SawFeedback = context.Context.Contains("ReadFile was rejected") && context.Context.Contains("ListFiles");
                calls = [new("ReadFile", JsonSerializer.SerializeToElement(new { path = "backend/Feature.cs" }))];
            }
            return calls == null ? result : result with { Output = result.Output with { ToolCalls = calls } };
        }
    }
    [Theory]
    [InlineData("ReadFile", ExecutorFailureCategory.InvalidArguments, 400, false, true)]
    [InlineData("ReadFile", ExecutorFailureCategory.InvalidArguments, 400, true, false)]
    [InlineData("ReadFile", ExecutorFailureCategory.Authorization, 403, false, false)]
    [InlineData("ReadFile", ExecutorFailureCategory.WorkspaceConflict, 409, true, false)]
    [InlineData("ReadFile", ExecutorFailureCategory.Infrastructure, 503, true, false)]
    [InlineData("WriteFile", ExecutorFailureCategory.InvalidArguments, 400, false, false)]
    public async Task OnlyKnownReadFileArgumentRejectionsBecomeCorrectiveFeedback(string operation, ExecutorFailureCategory category, int status, bool unknown, bool recoverable)
    {
        await using var factory = new PlatformFactory();
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var sandbox = new RejectRead(category, status, unknown);
        var tool = new EngineeringTool(operation, sandbox, new DeterministicSourceControl(), store);
        var context = new AgentContext(new Project { RepositoryConnection = new("owner", "repo", 1) }, new ExecutionRun(), "",
            new AgentDefinition("a", AgentRole.BackendEngineer, "a", "", "test", "", [], [operation]));
        var args = JsonSerializer.SerializeToElement(new { path = "missing.cs", content = "preserved" });
        if (recoverable)
        {
            var result = await tool.ExecuteAsync(context, args, "op", default);
            Assert.False(result.Success); Assert.Null(result.EvidenceId); Assert.Null(result.WorkspaceRevision);
            Assert.Contains("ListFiles", result.Output); Assert.Contains("new ReadFile", result.Output);
        }
        else await Assert.ThrowsAsync<ExecutorFailureException>(() => tool.ExecuteAsync(context, args, "op", default));
        Assert.Equal(1, sandbox.Calls);
    }

    private sealed class RejectRead(ExecutorFailureCategory category, int status, bool unknown) : IExecutionSandbox
    {
        public bool Configured => true;
        public int Calls;
        public Task<SandboxResponse> ExecuteAsync(SandboxRequest request, CancellationToken ct)
        {
            Calls++;
            throw new ExecutorFailureException(category, request.OperationId, request.Operation, status, unknown);
        }
    }
    private sealed class RestrictedExecutor : IExecutionSandbox
    {
        private readonly DeterministicSandbox inner = new();
        public int Failures;
        public bool Configured => true;
        public Task<SandboxResponse> ExecuteAsync(SandboxRequest request, CancellationToken ct)
        {
            if (request.Operation == "RunTests")
            {
                Failures++;
                throw new ExecutorFailureException(ExecutorFailureCategory.ProfileRestriction, request.OperationId, request.Operation, 422, false);
            }
            return inner.ExecuteAsync(request, ct);
        }
    }
}
