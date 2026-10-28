using AIEngineeringPlatform.Domain;
using Xunit;
namespace AIEngineeringPlatform.Domain.Tests;

public class ExecutionTests
{
    [Fact] public void CannotSkipQualityGates() => Assert.Throws<InvalidOperationException>(() => new ExecutionRun().Transition(RunStage.Completed));
    [Fact]
    public void PauseResumesCorrectStage()
    {
        var run = new ExecutionRun { CurrentStage = RunStage.RunningTests };
        var paused = run.Transition(RunStage.Paused);
        Assert.Equal(RunStage.RunningTests, paused.ResumeStage);
        Assert.Equal(RunStage.RunningTests, paused.Transition(paused.ResumeStage).CurrentStage);
    }
    [Fact] public void ReviewCannotCompleteDelivery() => Assert.Throws<InvalidOperationException>(() => new ExecutionRun { CurrentStage = RunStage.Reviewing }.Transition(RunStage.Completed));
    [Fact] public void CorrectionsConsumeIteration() => Assert.Equal(2, new ExecutionRun { CurrentStage = RunStage.Reviewing }.Transition(RunStage.Implementing).Iteration);
    [Fact] public void IterationLimitRejectsCorrection() => Assert.Throws<InvalidOperationException>(() => new ExecutionRun { CurrentStage = RunStage.Reviewing, Iteration = 5 }.Transition(RunStage.Implementing));
    [Fact]
    public void RetryResumesFailedStageAndIsBounded()
    {
        var failed = new ExecutionRun { CurrentStage = RunStage.RunningTests }.Transition(RunStage.Failed, "failure");
        var retry = failed.Transition(failed.ResumeStage);
        Assert.Equal(RunStage.RunningTests, retry.CurrentStage); Assert.Equal(1, retry.RetryCount);
        Assert.Throws<InvalidOperationException>(() => (failed with { RetryCount = 2 }).Transition(failed.ResumeStage));
    }
    [Fact] public void BudgetExhaustionStopsExecution() => Assert.Throws<InvalidOperationException>(() => new ExecutionRun { Cost = 5 }.CheckLimits(DateTimeOffset.UtcNow));
    [Fact] public void DurationExhaustionStopsExecution() => Assert.Throws<InvalidOperationException>(() => new ExecutionRun { StartedAt = DateTimeOffset.UtcNow.AddHours(-2) }.CheckLimits(DateTimeOffset.UtcNow));
    [Fact] public void ToolLimitStopsExecution() => Assert.Throws<InvalidOperationException>(() => new ExecutionRun { ToolCalls = 30 }.CheckLimits(DateTimeOffset.UtcNow));
    [Fact] public void PolicyRejectsUnsafeLimits() => Assert.Throws<ArgumentException>(() => new ExecutionPolicy(MaxIterations: 0).Validate());
    [Fact] public void AutomaticMergeRemainsDisabled() => Assert.Throws<ArgumentException>(() => new ExecutionPolicy(RequireHumanApprovalForMerge: false).Validate());
    [Fact] public void ProjectRequiresName() => Assert.Throws<ArgumentException>(() => new Project().Validate());
    [Fact] public void CompletedRunCannotResume() => Assert.Throws<InvalidOperationException>(() => new ExecutionRun { CurrentStage = RunStage.Completed }.Transition(RunStage.Implementing));
    [Fact] public void FailedRunCanBeCancelled() => Assert.Equal(RunStage.Cancelled, new ExecutionRun { CurrentStage = RunStage.Failed }.Transition(RunStage.Cancelled).CurrentStage);
}
