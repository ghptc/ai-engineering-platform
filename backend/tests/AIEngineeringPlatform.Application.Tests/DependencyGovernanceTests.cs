using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Application.Dependencies;
using AIEngineeringPlatform.Domain;
using NSubstitute;
using System.Text.Json;
using Xunit;
namespace AIEngineeringPlatform.Application.Tests;
public sealed class DependencyGovernanceTests
{
    public static DependencyProposal Proposal => new(1, "npm", "npm", "https://registry.npmjs.org", [new("is-number", "7.0.0", false, "Validate numeric input")], [], "License/security unknown until reviewed", ["Use built-in validation"], ["Write backend contracts"], ["Frontend numeric validation"], []);
    [Theory]
    [InlineData("--registry", "7.0.0", "https://registry.npmjs.org")]
    [InlineData("is-number", "latest", "https://registry.npmjs.org")]
    [InlineData("is-number", "^7.0.0", "https://registry.npmjs.org")]
    [InlineData("is-number", "https://evil.invalid/pkg.tgz", "https://registry.npmjs.org")]
    [InlineData("is-number", "7.0.0", "https://evil.invalid")]
    [InlineData("../escape", "7.0.0", "https://registry.npmjs.org")]
    public void MaliciousSourcesVersionsAndArgumentsAreRejected(string name, string version, string registry) => Assert.Throws<ArgumentException>(() => DependencyGovernance.Validate(Proposal with { Registry = registry, Packages = [new(name, version, false, "reason")] }));
    [Fact]
    public void WaitingStatePreservesStageAndLimits()
    {
        var run = new ExecutionRun { CurrentStage = RunStage.Planning, Cost = 1.2m, ToolCalls = 26, Iteration = 1 };
        var waiting = run.Transition(RunStage.AwaitingDependencyApproval);
        Assert.False(waiting.IsRunnable); Assert.Equal(RunStage.Planning, waiting.ResumeStage);
        var resumed = waiting.Transition(waiting.ResumeStage);
        Assert.Equal(run.Policy, resumed.Policy); Assert.Equal(run.Cost, resumed.Cost); Assert.Equal(26, resumed.ToolCalls); Assert.Equal(1, resumed.Iteration); Assert.Equal(0, resumed.RetryCount);
    }
    [Fact]
    public async Task RequestsAreDeduplicatedBeforeAnotherModelTurnOrExecutorCall()
    {
        var approvals = Substitute.For<IDependencyApprovalStore>(); var store = Substitute.For<IPlatformStore>(); var executor = Substitute.For<IDependencyExecutor>();
        DependencyApprovalRequest? stored = null;
        approvals.GetAsync(Arg.Any<string>(), default).Returns(_ => stored);
        approvals.SaveAsync(Arg.Any<DependencyApprovalRequest>(), null, default).Returns(x => { stored = x.Arg<DependencyApprovalRequest>(); return true; });
        var service = new DependencyGovernance(approvals, store, executor); var repository = new RepositoryConnection("test-owner", "aep-fixture", 1);
        var project = new Project { Id = "project", Name = "Fixture", RepositoryConnection = repository };
        var run = new ExecutionRun { Id = "run", ProjectId = project.Id, CurrentStage = RunStage.Planning };
        var definition = new AgentDefinition("Architect", AgentRole.Architect, "Architect", "", "fixture", "", [], ["RequestDependencyApproval"]);
        var context = new AgentContext(project, run, "", definition, "task");
        var args = JsonSerializer.SerializeToElement(Proposal, DependencyGovernance.StrictJson);
        var first = await service.RequestAsync(context, args, "operation-1", default);
        var second = await service.RequestAsync(context, args, "operation-2", default);
        Assert.Equal(first.Id, second.Id); Assert.Equal("operation-1", second.OriginatingOperationId);
        await executor.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default!, default);
        await approvals.Received(1).SaveAsync(Arg.Any<DependencyApprovalRequest>(), null, default);
    }
    [Fact]
    public async Task ExpiredOrExhaustedExecutionCannotResumeAfterOwnerApproval()
    {
        var approvals = Substitute.For<IDependencyApprovalStore>(); var store = Substitute.For<IPlatformStore>(); var executor = Substitute.For<IDependencyExecutor>();
        var request = new DependencyApprovalRequest("id", "project", "run", RunStage.Planning, AgentRole.Architect, "task", "op", 0, Proposal, DependencyApprovalStatus.Rejected, 1, DateTimeOffset.UtcNow, []);
        approvals.GetAsync("id", default).Returns(request); approvals.ListAsync("project", "run", default).Returns(new[] { DependencyRequestSummary.From(request) });
        var run = new ExecutionRun { Id = "run", ProjectId = "project", CurrentStage = RunStage.AwaitingDependencyApproval, ResumeStage = RunStage.Planning, StartedAt = DateTimeOffset.UtcNow.AddHours(-2) };
        store.GetRunAsync("run", default).Returns(run);
        var service = new DependencyGovernance(approvals, store, executor);
        await Assert.ThrowsAsync<ExecutionLimitException>(() => service.ResumeAsync("id", default));
        await store.DidNotReceiveWithAnyArgs().SaveRunAsync(default!, default, default, default);
    }
    [Fact]
    public async Task RegistryAccessRequiresExplicitAdditionalOwnerAuthorization()
    {
        var approvals = Substitute.For<IDependencyApprovalStore>(); var store = Substitute.For<IPlatformStore>(); var executor = Substitute.For<IDependencyExecutor>();
        var repository = new RepositoryConnection("test-owner", "aep-fixture", 1); var project = new Project { Id = "project", RepositoryConnection = repository };
        var request = new DependencyApprovalRequest("id", "project", "run", RunStage.Planning, AgentRole.Architect, "task", "op", 0, Proposal, DependencyApprovalStatus.Pending, 0, DateTimeOffset.UtcNow, []);
        approvals.GetAsync("id", default).Returns(request); store.GetProjectAsync("project", default).Returns(project);
        store.GetRunAsync("run", default).Returns(new ExecutionRun { Id = "run", ProjectId = "project", ProjectSnapshot = project, CurrentStage = RunStage.AwaitingDependencyApproval, ResumeStage = RunStage.Planning });
        var service = new DependencyGovernance(approvals, store, executor);
        await Assert.ThrowsAsync<ArgumentException>(() => service.DecideAsync("id", 0, "Resolve", [], false, "Inspect packages", "owner", default));
        await executor.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default!, default);
    }
    [Theory]
    [InlineData(true, DependencyApprovalStatus.ResolutionFailed)]
    [InlineData(false, DependencyApprovalStatus.Resolving)]
    public async Task FailuresPersistKnownOutcomeAndUnknownOperationsCannotReplay(bool known, DependencyApprovalStatus expected)
    {
        var approvals = Substitute.For<IDependencyApprovalStore>(); var store = Substitute.For<IPlatformStore>(); var executor = Substitute.For<IDependencyExecutor>();
        var repository = new RepositoryConnection("test-owner", "aep-fixture", 1); var project = new Project { Id = "project", RepositoryConnection = repository };
        var request = new DependencyApprovalRequest("id", "project", "run", RunStage.Planning, AgentRole.Architect, "task", "op", 0, Proposal, DependencyApprovalStatus.Pending, 0, DateTimeOffset.UtcNow, []);
        approvals.GetAsync("id", default).Returns(_ => request);
        approvals.SaveAsync(Arg.Any<DependencyApprovalRequest>(), Arg.Any<long?>(), default).Returns(x => { request = x.Arg<DependencyApprovalRequest>(); return true; });
        store.GetProjectAsync("project", default).Returns(project);
        store.GetRunAsync("run", default).Returns(new ExecutionRun { Id = "run", ProjectId = "project", ProjectSnapshot = project, CurrentStage = RunStage.AwaitingDependencyApproval, ResumeStage = RunStage.Planning });
        executor.ResolveAsync(Arg.Any<DependencyApprovalRequest>(), repository, default).Returns<DependencyResolution>(_ => throw new DependencyAcquisitionException(new("registry_response", "dependency_registry_response_limit", true, known, known ? "RepairConfiguration" : "Reconcile")));
        var service = new DependencyGovernance(approvals, store, executor);
        var failed = await service.DecideAsync("id", 0, "Resolve", [], true, "Explicit resolution authorization", "owner", default);
        Assert.Equal(expected, failed.Status); Assert.Contains("dependency_registry_response_limit", failed.Diagnostic);
        Assert.Single(failed.Audit); Assert.Equal("owner", failed.Audit[0].Owner);
        await Assert.ThrowsAsync<ArgumentException>(() => service.DecideAsync("id", failed.Version, "RetryResolution", [], true, "Blind replay denied", "owner", default));
        await executor.Received(1).ResolveAsync(Arg.Any<DependencyApprovalRequest>(), repository, default);
        if (!known)
        {
            executor.ReconcileAsync("id", default).Returns<DependencyResolution?>(_ => throw new DependencyAcquisitionException(new("reconciliation", "dependency_acquisition_interrupted", true, true, "RepairConfiguration")));
            var reconciled = await service.DecideAsync("id", failed.Version, "Reconcile", [], false, "Read and reconcile the original attempt", "owner", default);
            Assert.Equal(DependencyApprovalStatus.ResolutionFailed, reconciled.Status);
            Assert.Equal(2, reconciled.Audit.Length); Assert.False(reconciled.Audit.Last().RegistryAccess);
            await executor.Received(1).ResolveAsync(Arg.Any<DependencyApprovalRequest>(), repository, default);
        }
        await store.DidNotReceiveWithAnyArgs().SaveRunAsync(default!, default, default, default);
    }
}
