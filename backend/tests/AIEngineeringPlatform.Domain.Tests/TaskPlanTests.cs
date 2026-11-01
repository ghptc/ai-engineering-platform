using AIEngineeringPlatform.Domain;
using Xunit;
namespace AIEngineeringPlatform.Domain.Tests;
public class TaskPlanTests
{
    [Fact]
    public void AdditionalSpecialistsRequireExplicitSelectionAndPreserveLegacyRoleIdentities()
    {
        Assert.Equal(6, (int)AgentRole.CodeReviewer); Assert.Equal(7, (int)AgentRole.SecurityEngineer); Assert.Equal(8, (int)AgentRole.InfrastructureEngineer);
        var task = new EngineeringTask("Container readiness", "Implement authorized repository configuration", ["Configuration is reviewed"], RequiresInfrastructure: true, RequiresSecurityReview: true);
        Assert.Equal(new[] { AgentRole.InfrastructureEngineer, AgentRole.BackendEngineer, AgentRole.TestEngineer, AgentRole.QAEngineer, AgentRole.SecurityEngineer, AgentRole.CodeReviewer }, task.Plan);
        Assert.DoesNotContain(AgentRole.InfrastructureEngineer, new ExecutionRun().SelectedAgents);
        Assert.DoesNotContain(AgentRole.SecurityEngineer, new ExecutionRun().SelectedAgents);
    }
    [Fact]
    public void SimpleTaskSkipsDesignAndKeepsValidationSpecialists()
    {
        var task = new EngineeringTask("Answer", "Change answer to 2", ["Tests assert 2"]);
        task.Validate();
        Assert.Equal(new[] { AgentRole.BackendEngineer, AgentRole.TestEngineer, AgentRole.QAEngineer, AgentRole.CodeReviewer }, task.Plan);
        var run = new ExecutionRun { Tasks = [task] };
        Assert.Equal(RunStage.Implementing, run.Transition(RunStage.Implementing).CurrentStage);
    }
    [Fact]
    public void DesignTaskIncludesBothDesignSpecialists()
    {
        var task = new EngineeringTask("New app", "Define and implement", ["Approved requirements"], true, true, [AgentRole.BackendEngineer, AgentRole.FrontendEngineer]);
        Assert.Equal(new[] { AgentRole.BusinessAnalyst, AgentRole.Architect, AgentRole.BackendEngineer, AgentRole.FrontendEngineer, AgentRole.TestEngineer, AgentRole.QAEngineer, AgentRole.CodeReviewer }, task.Plan);
    }
    [Fact]
    public void AdvancingTaskPreservesCumulativeBudgetsAndRequiresDeliveryStage()
    {
        var task = new EngineeringTask("Answer", "Change answer", ["Tests pass"]);
        var run = new ExecutionRun { Tasks = [task, task], CurrentStage = RunStage.PreparingDelivery, Cost = 1, TokenUsage = 50, ToolCalls = 9 };
        var next = run.AdvanceTask();
        Assert.Equal(1, next.TaskIndex); Assert.Equal(2, next.Iteration); Assert.Equal(RunStage.Pending, next.CurrentStage);
        Assert.Equal(run.Policy, next.Policy); Assert.Equal(run.Cost, next.Cost); Assert.Equal(run.ToolCalls, next.ToolCalls); Assert.Equal(run.StartedAt, next.StartedAt);
        Assert.Throws<InvalidOperationException>(() => (run with { CurrentStage = RunStage.AwaitingHumanApproval }).AdvanceTask());
        Assert.Throws<InvalidOperationException>(() => (run with { Policy = new(MaxIterations: 1) }).AdvanceTask());
    }
}
