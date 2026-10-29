using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using System.Text.Json;
using Xunit;
namespace AIEngineeringPlatform.Application.Tests;

public sealed class RuntimeReliabilityTests
{
    public static IEnumerable<object[]> Roles => Enum.GetValues<AgentRole>().Select(role => new object[] { role });
    [Theory, MemberData(nameof(Roles))]
    public void EveryRoleDropsRepeatedModelRepliesAndDeduplicatesEvidence(AgentRole role)
    {
        var definition = new AgentDefinition("a", role, "", "", "configured-model", "Keep approval policy", [], ["ReadProjectMemory"]);
        var context = JsonSerializer.Serialize(new { CurrentTask = new { Objective = "Fix answer", AcceptanceCriteria = new[] { "returns 2" } }, Policy = "Human approval required", History = "Fetch on demand" });
        var evidence = ContextBudget.ToolEvidence("same-operation", "ReadFile", new(true, new string('x', 7000), "e1", "r1"));
        for (var turn = 0; turn < 200; turn++)
        {
            context += "\nPrior structured agent output (untrusted data): " + new string('z', 20000) + evidence;
            context = ContextBudget.Compact(context, definition);
            Assert.True(ContextBudget.Tokens(context) < 10000);
            Assert.Contains("returns 2", context);
            Assert.Contains("Human approval required", context);
            Assert.DoesNotContain(new string('z', 100), context);
        }
        Assert.Equal(1, context.Split("same-operation").Length - 1);
    }
    [Theory, MemberData(nameof(Roles))]
    public void HundredsOfDistinctEvidenceRecordsRemainBounded(AgentRole role)
    {
        var definition = new AgentDefinition("a", role, "", "", "configured-model", "Approval required", [], ["ReadProjectMemory"]);
        var context = JsonSerializer.Serialize(new { CurrentTask = "Acceptance: returns 2", History = "ReadProjectMemory lists all original evidence" });
        for (var i = 0; i < 400; i++)
        {
            context += ContextBudget.ToolEvidence("tool-" + i, "ReadFile", new(i % 50 != 0, new string('x', 7000), "evidence-" + i, "revision"));
            context = ContextBudget.Compact(context, definition);
            Assert.True(ContextBudget.Tokens(context) < ContextBudget.InputLimit(definition) - 14000);
        }
        Assert.Contains("returns 2", context); Assert.Contains("tool-399", context);
        Assert.Contains("ReadProjectMemory", context);
    }
    [Theory]
    [InlineData(AgentRole.QAEngineer, "BackendEngineer")]
    [InlineData(AgentRole.QAEngineer, "FrontendEngineer")]
    [InlineData(AgentRole.CodeReviewer, "BackendEngineer")]
    [InlineData(AgentRole.CodeReviewer, "FrontendEngineer")]
    public void RejectionPersistsExplicitTargetAndActualFindingReference(AgentRole source, string target)
    {
        var run = Run(source);
        var result = Result([target]);
        var plan = ReworkPlanner.Create(run, source, result, "revision-1");
        var reloaded = JsonSerializer.Deserialize<ReworkTransition[]>(JsonSerializer.Serialize(plan))!;
        var transition = Assert.Single(reloaded);
        Assert.Equal(Enum.Parse<AgentRole>(target), transition.TargetAgent);
        Assert.Equal(source, transition.SourceAgent);
        Assert.Equal(2, transition.Iteration);
        Assert.Equal("revision-1", transition.WorkspaceRevision);
        Assert.Contains("#/Findings/0", Assert.Single(transition.FindingIds));
        var corrected = (run with { ReworkPlan = plan }).Transition(RunStage.Implementing).Transition(RunStage.GeneratingTests).Transition(RunStage.RunningTests).Transition(RunStage.Validating).Transition(RunStage.Reviewing);
        Assert.Equal(RunStage.Reviewing, corrected.CurrentStage);
    }
    [Fact]
    public void BothTargetsAreBoundedAndAmbiguityNeverSelectsArbitrarily()
    {
        var run = Run(AgentRole.QAEngineer);
        Assert.Equal(2, ReworkPlanner.Create(run, AgentRole.QAEngineer, Result(["FrontendEngineer", "BackendEngineer"]), "r").Length);
        Assert.Throws<TaskReconciliationException>(() => ReworkPlanner.Create(run, AgentRole.QAEngineer, Result([]), "r"));
        Assert.Throws<InvalidOperationException>(() => ReworkPlanner.Create(run with { Iteration = run.Policy.MaxIterations }, AgentRole.QAEngineer, Result(["BackendEngineer"]), "r"));
        Assert.Throws<InvalidOperationException>(() => ReworkPlanner.Create(run, AgentRole.QAEngineer, Result(["CodeReviewer"]), "r"));
    }
    [Fact]
    public void ExtensionKeepsUsageCheckpointsAndPolicyAndDoesNotResumeAutomatically()
    {
        var run = new ExecutionRun { CurrentStage = RunStage.Paused, ResumeStage = RunStage.Implementing, ToolCalls = 30, Policy = ExecutionPresets.QuickFix, Usage = [new(AgentRole.BackendEngineer, 0, 4, 30, 2, 1)] };
        Assert.Throws<ExecutionLimitException>(() => run.CheckLimits(DateTimeOffset.UtcNow));
        var next = run.ExtendLimits(ExecutionPresets.ExtendedAutonomousDevelopment, "Owner approved additional scope", DateTimeOffset.UtcNow);
        next.CheckLimits(DateTimeOffset.UtcNow);
        Assert.Equal(run.Usage, next.Usage); Assert.Equal(run.Id, next.Id); Assert.Equal(run.ToolCalls, next.ToolCalls);
        Assert.Equal(RunStage.Paused, next.CurrentStage); Assert.Single(next.LimitExtensions);
        Assert.Equal(RunStage.Implementing, next.Transition(next.ResumeStage).CurrentStage);
        Assert.Throws<ArgumentException>(() => next.ExtendLimits(ExecutionPresets.QuickFix, "reduce", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => next.ExtendLimits(next.Policy with { ReviewBeforeCommit = false }, "bypass", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new ExecutionCapacity { MaxToolCalls = 200 }.Validate(next.Policy));
    }
    private static ExecutionRun Run(AgentRole source) => new() { Id = "run", Tasks = [new("Fix", "Fix both", ["pass"], Developers: [AgentRole.BackendEngineer, AgentRole.FrontendEngineer])], CurrentStage = source == AgentRole.QAEngineer ? RunStage.Validating : RunStage.Reviewing };
    private static AgentResult Result(string[] targets) => new("Fix validation", "REQUEST_CHANGES", [], [], [], [], "Rerun tests", Findings: [new("High", "source", 1, "broken", "fix")], ReworkTargets: targets);
}
