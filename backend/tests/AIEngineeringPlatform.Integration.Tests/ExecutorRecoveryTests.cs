using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
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
