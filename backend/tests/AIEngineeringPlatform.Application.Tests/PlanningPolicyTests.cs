using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using Xunit;
namespace AIEngineeringPlatform.Application.Tests;

public class PlanningPolicyTests
{
    private static AgentResult Backlog => new("README-only planning", "BLOCKED", [], [], [], [], "Architect next",
        Requirements: [new("M01", "Bootstrap", "As owner", ["Tested slice"], 1, [], false)],
        Planning: new(["Dependency availability unverified"], ["Verify offline cache"], [new("Implementation", "No approved test profile", "repository-evidence")], "", "", ""));

    [Fact]
    public void ExplicitFutureExecutionBlockerAllowsBusinessPlanningHandoff()
        => Assert.True(PlanningPolicy.DefersExecutionBlockers(AgentRole.BusinessAnalyst, Backlog));

    [Theory]
    [InlineData("Planning", "repository-evidence")]
    [InlineData("Implementation", "")]
    [InlineData("Unknown", "repository-evidence")]
    public void GenuinePlanningOrAmbiguousBlockerRemainsBlocked(string stage, string evidence)
        => Assert.False(PlanningPolicy.DefersExecutionBlockers(AgentRole.BusinessAnalyst, Backlog with { Planning = Backlog.Planning! with { Blockers = [new(stage, "Cannot continue", evidence)] } }));

    [Fact]
    public void LegacyBlockersMissingBacklogAndHumanDecisionsCannotBeOverridden()
    {
        Assert.False(PlanningPolicy.DefersExecutionBlockers(AgentRole.BusinessAnalyst, Backlog with { Planning = null }));
        Assert.False(PlanningPolicy.DefersExecutionBlockers(AgentRole.BusinessAnalyst, Backlog with { Requirements = [] }));
        Assert.False(PlanningPolicy.DefersExecutionBlockers(AgentRole.BusinessAnalyst, Backlog with { Decision = "AWAITING_HUMAN_INPUT" }));
        Assert.False(PlanningPolicy.DefersExecutionBlockers(AgentRole.BackendEngineer, Backlog));
    }

    [Fact]
    public void ArchitectMustAddressBootstrapDependenciesAndTestsBeforeDeferredHandoff()
    {
        var architecture = Backlog with { ArchitectureDecisions = [new("M01", "README only", "Bootstrap", "Offline", "")] };
        Assert.False(PlanningPolicy.DefersExecutionBlockers(AgentRole.Architect, architecture));
        Assert.True(PlanningPolicy.DefersExecutionBlockers(AgentRole.Architect, architecture with { Planning = architecture.Planning! with { BootstrapStrategy = "WriteFile", DependencyStrategy = "Verify approved offline cache", TestStrategy = "Approved backend/frontend profiles with revision evidence" } }));
    }
}
