using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;

public sealed partial class RuntimeFlowTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DevPulseCommandRejectionPreservesTwoWritesAndResumeIsExplicit(bool uncertain)
    {
        await using var root = new PlatformFactory(); var sandbox = new DevPulseRejectedSandbox(uncertain);
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider, DevPulseBootstrapModel>();
            services.RemoveAll<IExecutionSandbox>(); services.AddSingleton<IExecutionSandbox>(sandbox);
        }));
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var run = await Start(client, store, ExecutionPresets.StandardDevelopment);
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.Paused, run.CurrentStage); Assert.Equal(0, run.RetryCount);
        var writes = (await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 100, default)).Where(x => x.Name == "WriteFile").ToArray();
        Assert.Equal(2, writes.Length); Assert.All(writes, x => Assert.Equal("Succeeded", x.Status));
        var failure = Assert.Single(await store.RecordsAsync("event", run.ProjectId, run.Id, null, 0, 0, 1000, default), x => x.Name == "ExecutorOperationBlocked");
        // Preserve the legacy ID shape as well: this is the record that collided in DevPulse.
        await store.AppendAsync(failure with { Id = "executor-failure-" + failure.Metadata["operationId"] }, default);
        var version = run.Version;
        (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/resume", new { })).EnsureSuccessStatusCode();
        run = await Wait(store, run.Id, r => !r.IsRunnable && r.Version > version);
        Assert.Equal(RunStage.Paused, run.CurrentStage); Assert.Equal(0, run.RetryCount);
        Assert.Equal(uncertain ? 1 : 2, sandbox.Failures);
        if (uncertain) Assert.Contains("no new attempt was sent", run.FailureReason);
        else Assert.Contains("pinned workload image", run.FailureReason);
        foreach (var write in writes)
        {
            var preserved = await store.GetRecordAsync("tool", write.Id, default);
            Assert.Equal(write.Content, preserved!.Content); Assert.Equal(write.Sequence, preserved.Sequence);
        }
        var failures = (await store.RecordsAsync("event", run.ProjectId, run.Id, null, 0, 0, 1000, default)).Where(x => x.Name == "ExecutorOperationBlocked").ToArray();
        Assert.Equal(uncertain ? 2 : 3, failures.Length);
        Assert.All(failures, x => Assert.Equal(failure.Metadata["operationId"], x.Metadata["operationId"]));
        Assert.DoesNotContain("Mongo", run.FailureReason);
    }

    [Fact]
    public async Task SecondaryRealMongoConflictDoesNotMaskOriginal409CommandRejection()
    {
        await using var root = new PlatformFactory(); var sandbox = new DevPulseRejectedSandbox(true); var logs = new CaptureExecutorLogs();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.AddLogging(builder => builder.AddProvider(logs));
            var actual = (MongoPlatformStore)services.Single(x => x.ServiceType == typeof(IPlatformStore)).ImplementationInstance!;
            var proxy = DispatchProxy.Create<IPlatformStore, FailureEvidenceStore>(); ((FailureEvidenceStore)proxy).Inner = actual;
            services.RemoveAll<IPlatformStore>(); services.AddSingleton(proxy);
            services.RemoveAll<IWorkspaceQueries>(); services.AddSingleton<IWorkspaceQueries>(actual);
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider, DevPulseBootstrapModel>();
            services.RemoveAll<IExecutionSandbox>(); services.AddSingleton<IExecutionSandbox>(sandbox);
        }));
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var run = await Start(client, store, ExecutionPresets.StandardDevelopment);
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.Paused, run.CurrentStage); Assert.Equal("ExecutorWorkspaceConflict", run.FailureCategory);
        Assert.Contains("HTTP 409", run.FailureReason); Assert.Contains("MongoDB code 11000, index _id_", run.FailureReason);
        Assert.Equal(1, sandbox.Failures); Assert.Equal(0, run.RetryCount);
        Assert.Contains(logs.Messages, x => x.Contains("HTTP 409") && x.Contains("evidencePersistenceFailed True"));
        Assert.Contains(logs.Messages, x => x.Contains("code 11000, index _id_"));
        Assert.Equal(2, (await store.RecordsAsync("tool", run.ProjectId, run.Id, null, 0, 0, 100, default)).Count(x => x.Name == "WriteFile"));
    }

    private sealed class CaptureExecutorLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
        public void Dispose() { }
        private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, error));
        }
    }
    public class FailureEvidenceStore : DispatchProxy
    {
        public IPlatformStore Inner { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IPlatformStore.AppendAsync) && args![0] is RecordEntry { Name: "ExecutorOperationBlocked" } entry)
                return ConflictAsync(entry, (CancellationToken)args[1]!);
            return method.Invoke(Inner, args);
        }
        private async Task ConflictAsync(RecordEntry entry, CancellationToken ct)
        {
            await Inner.AppendAsync(entry with { Content = "Test-only immutable conflict" }, ct);
            await Inner.AppendAsync(entry, ct); // Real MongoDB 11000, not a synthetic driver exception.
        }
    }
    private sealed class DevPulseBootstrapModel : ILanguageModelProvider
    {
        private readonly DeterministicModel inner = new();
        public async Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
        {
            var result = await inner.ExecuteAsync(context, ct);
            if (context.Definition.Role != AgentRole.BackendEngineer) return result;
            return result with { Output = result.Output with { ToolCalls = [
                new("WriteFile", JsonSerializer.SerializeToElement(new { path = "src/DevPulse.Core/DevPulse.Core.csproj", content = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>" })),
                new("WriteFile", JsonSerializer.SerializeToElement(new { path = "src/DevPulse.Api/DevPulse.Api.csproj", content = "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>" })),
                new("RunCommand", JsonSerializer.SerializeToElement(new { profile = "build" }))
            ] } };
        }
    }
    private sealed class DevPulseRejectedSandbox(bool uncertain) : IExecutionSandbox
    {
        private readonly DeterministicSandbox inner = new();
        public int Failures;
        public bool Configured => true;
        public Task<SandboxResponse> ExecuteAsync(SandboxRequest request, CancellationToken ct)
        {
            if (request.Operation == "RunCommand")
            {
                Failures++;
                Assert.Equal("build", request.Arguments.GetProperty("profile").GetString());
                throw new ExecutorFailureException(uncertain ? ExecutorFailureCategory.WorkspaceConflict : ExecutorFailureCategory.Infrastructure, request.OperationId, request.Operation, uncertain ? 409 : 503, uncertain, uncertain ? ExecutorFailureReason.Unspecified : ExecutorFailureReason.ImageUnavailable);
            }
            return inner.ExecuteAsync(request, ct);
        }
    }
}
