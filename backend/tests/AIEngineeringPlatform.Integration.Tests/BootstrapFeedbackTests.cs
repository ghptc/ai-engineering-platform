using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Agents;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using NSubstitute;
using System.Text.Json;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;
public sealed class BootstrapFeedbackTests
{
    [Theory]
    [InlineData(400, false, true)] [InlineData(400, true, false)]
    [InlineData(401, false, false)] [InlineData(409, true, false)] [InlineData(503, true, false)]
    public async Task OnlyKnownPreWriteValidationFailuresBecomeCorrectiveFeedback(int status, bool unknown, bool feedback)
    {
        const string operation = "bootstrap-attempt";
        var category = status == 400 ? ExecutorFailureCategory.InvalidArguments : status == 401 ? ExecutorFailureCategory.Authorization : status == 409 ? ExecutorFailureCategory.WorkspaceConflict : ExecutorFailureCategory.Infrastructure;
        var failure = new ExecutorFailureException(category, operation, "PrepareFrontendLock", status, unknown);
        var sandbox = Substitute.For<IExecutionSandbox>();
        sandbox.ExecuteAsync(Arg.Any<SandboxRequest>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<SandboxResponse>(failure));
        var definition = new AgentDefinition("frontend", AgentRole.FrontendEngineer, "Frontend", "Bootstrap", "configured-model", "Use controlled tools", [], ["PrepareFrontendLock"]);
        var context = new AgentContext(new() { RepositoryConnection = new("owner", "repo", 1) }, new(), "", definition);
        var tool = new EngineeringTool("PrepareFrontendLock", sandbox, Substitute.For<ISourceControl>(), Substitute.For<IPlatformStore>());
        if (feedback)
        {
            var result = await tool.ExecuteAsync(context, JsonSerializer.SerializeToElement(new { }), operation, default);
            Assert.False(result.Success); Assert.Null(result.EvidenceId); Assert.Contains("preserved", result.Output); Assert.Contains("ApprovedDependencies", result.Output);
        }
        else Assert.Same(failure, await Assert.ThrowsAsync<ExecutorFailureException>(() => tool.ExecuteAsync(context, JsonSerializer.SerializeToElement(new { }), operation, default)));
        await sandbox.Received(1).ExecuteAsync(Arg.Is<SandboxRequest>(x => x.OperationId == operation && x.Operation == "PrepareFrontendLock"), Arg.Any<CancellationToken>());
    }
    [Fact] public void BootstrapDoesNotExpandReadOnlyOrBackendRoleTools()
    {
        Assert.Contains("PrepareFrontendLock", AgentCatalog.Tools(AgentRole.FrontendEngineer));
        Assert.Contains("PrepareFrontendLock", AgentCatalog.Tools(AgentRole.TestEngineer));
        foreach (var role in new[] { AgentRole.BackendEngineer, AgentRole.QAEngineer, AgentRole.SecurityEngineer, AgentRole.CodeReviewer, AgentRole.InfrastructureEngineer }) Assert.DoesNotContain("PrepareFrontendLock", AgentCatalog.Tools(role));
    }
}
