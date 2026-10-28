using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using NSubstitute;
using System.Text.Json;
using Xunit;

namespace AIEngineeringPlatform.Application.Tests;
public class ContextBudgetTests
{
    [Fact]
    public async Task ArchitectContextDoesNotGrowWithHistoricalAgentProposals()
    {
        var store = Substitute.For<IPlatformStore>();
        store.RecordsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<int>(), default).Returns(Array.Empty<RecordEntry>());
        var proposal = new RequirementProposal("Change answer", "Return 2", "As an owner", ["answer() returns 2", "Run the updated test"], 1, [], false);
        var result = new AgentResult("Historical agent transcript " + new string('h', 5000), "CONTINUE", ["answer() returns 2"], [], [], [], new string('n', 10000), [proposal], ArchitectureDecisions: []);
        var history = Enumerable.Range(0, 500).Select(i => new RecordEntry { Id = "old-" + i, Kind = "message", Name = "BusinessAnalyst", RunId = "previous", Content = JsonSerializer.Serialize(result with { Requirements = [proposal with { Description = new string('p', 4000) }] }) }).ToArray();
        var human = new RecordEntry { Id = "human", Kind = "message", Name = "Client", Content = "Only change the disposable JavaScript fixture. Never touch the default branch." };
        store.RecordsAsync("message", "p", null, null, 0, 0, 1000, default).Returns(history.Append(human).ToArray());
        store.RecordsAsync("requirement", "p", null, null, 0, 0, 1000, default).Returns(new[] { new RecordEntry { Id = "selected", Kind = "requirement", Name = proposal.Title, Content = JsonSerializer.Serialize(proposal) }, new RecordEntry { Id = "unrelated", Kind = "requirement", Content = "Unrelated backlog " + new string('u', 100000) } });
        store.RecordsAsync("artifact", "p", null, null, 0, 0, 1000, default).Returns(new[] { new RecordEntry { Id = "analysis", Kind = "artifact", Name = "Requirements", RunId = "r", Content = JsonSerializer.Serialize(result) } });
        var run = new ExecutionRun { Id = "r", ProjectId = "p", RequirementIds = ["selected"], CurrentStage = RunStage.Planning };
        var context = await new ContextBuilder(store).BuildAsync(new Project { Id = "p", OriginalContext = "Change answer() from 1 to 2" }, run, default);
        var prepared = ContextBudget.Compact(context, Agent(128000, 25000) with { Role = AgentRole.Architect });
        Assert.True(ContextBudget.Tokens(prepared) < 10000);
        Assert.Contains("answer() returns 2", prepared); Assert.Contains("Run the updated test", prepared);
        Assert.Contains("Never touch the default branch", prepared);
        Assert.DoesNotContain("Historical agent transcript", prepared); Assert.DoesNotContain("Unrelated backlog", prepared);
        Assert.Equal(1, prepared.Split("Return 2").Length - 1);
        Assert.Equal(5, run.Policy.MaxBudgetUsd); Assert.Equal(0, run.Cost);
    }
    private static AgentDefinition Agent(int window = 128000, int input = 96000, int output = 4000) => new("a", AgentRole.BackendEngineer, "Backend", "", "configured-model", "Trusted instructions", [], ["ReadProjectMemory"], MaxOutputTokens: output, ModelContextTokens: window, MaxInputTokens: input);
    [Fact]
    public void UnicodeUsesConservativeTokenBoundAndOutputIsReserved()
    {
        Assert.Equal(4, ContextBudget.Tokens("😀"));
        Assert.Equal(16000, ContextBudget.InputLimit(Agent(20000, 19000)));
        Assert.Throws<InvalidOperationException>(() => ContextBudget.Validate(Agent(20000, 19000), new string('x', 16000), "instructions"));
    }
    [Fact]
    public void CompactionPreservesProtectedContentAndCurrentEvidence()
    {
        var context = JsonSerializer.Serialize(new { OriginalContext = "Must encrypt data", Requirements = new[] { "Acceptance: no plaintext" }, Decisions = "MongoDB", HumanInstructions = "Approval required", History = new string('x', 180000) }) + "\nTool evidence: failed tests evidence-123";
        var compacted = ContextBudget.Compact(context, Agent());
        Assert.Contains("Must encrypt data", compacted);
        Assert.Contains("Acceptance: no plaintext", compacted);
        Assert.Contains("MongoDB", compacted);
        Assert.Contains("Approval required", compacted);
        Assert.Contains("failed tests evidence-123", compacted);
        Assert.Contains("ReadProjectMemory", compacted);
        Assert.True(ContextBudget.Tokens(compacted) < 96000);
    }
    [Fact]
    public void ProtectedOverflowFailsExplicitlyInsteadOfTruncating()
    {
        var context = JsonSerializer.Serialize(new { OriginalContext = new string('x', 180000), History = "old" });
        Assert.Contains("No protected requirements were discarded", Assert.Throws<InvalidOperationException>(() => ContextBudget.Compact(context, Agent())).Message);
    }
    [Fact]
    public void ToolCompactionRetainsFailureRevisionAndRetrievableId()
    {
        var text = ContextBudget.ToolEvidence("tool-1", "RunTests", new(false, new string('x', 200000), "evidence-1", "revision-1"));
        Assert.Contains("tool-1", text); Assert.Contains("revision-1", text); Assert.Contains("evidence-1", text);
        Assert.Contains("\"Success\":false", text); Assert.Contains("ReadProjectMemory", text); Assert.True(text.Length < 4000);
    }
    [Fact]
    public void LongToolLoopCompactsEarlierOutputsWithoutLosingEvidenceStatus()
    {
        var context = JsonSerializer.Serialize(new { Requirements = "Keep acceptance criteria", History = "old history" });
        for (var i = 0; i < 30; i++) context += ContextBudget.ToolEvidence("tool-" + i, "ReadFile", new(i != 0, new string('x', 7000), "evidence-" + i, "revision"));
        var compacted = ContextBudget.Compact(context, Agent());
        Assert.Contains("Keep acceptance criteria", compacted);
        Assert.Contains("\"Success\":false", compacted);
        Assert.Contains("tool-0", compacted); Assert.Contains("tool-29", compacted);
        Assert.Contains("evidence-0", compacted); Assert.Contains("evidence-29", compacted);
        Assert.True(ContextBudget.Tokens(compacted) < 96000);
    }
    [Fact]
    public void TestReportSummaryPreservesActualEvidenceAndRevision()
    {
        var summary = JsonSerializer.Serialize(ContextBudget.Summary(new RecordEntry { Kind = "artifact", Name = "TestReport", Content = JsonSerializer.Serialize(new ToolResult(false, new string('x', 200000), "test-evidence", "tested-revision")) }));
        Assert.Contains("test-evidence", summary); Assert.Contains("tested-revision", summary);
        Assert.Contains("\"Success\":false", summary); Assert.True(summary.Length < 2000);
    }
    [Fact]
    public async Task LargeSnapshotsDoNotAccumulateAcrossStages()
    {
        var store = Substitute.For<IPlatformStore>();
        var project = new Project { Id = "p", Name = "Project", OriginalContext = "Current requirements and instructions" };
        var run = new ExecutionRun { Id = "r", ProjectId = "p" };
        store.RecordsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<int>(), default).Returns(Array.Empty<RecordEntry>());
        var snapshots = Enumerable.Range(0, 30).Select(i => new RecordEntry { Kind = "artifact", Name = i % 2 == 0 ? "DevelopmentContext" : "NextContext", Content = new string('x', 200000), RunId = "r" }).ToArray();
        store.RecordsAsync("artifact", "p", null, null, 0, 0, 1000, default).Returns(snapshots);
        store.RecordsAsync("message", "p", null, null, 0, 0, 1000, default).Returns(new[] { new RecordEntry { Name = "Client", Content = "Do not publish without approval" }, new RecordEntry { Name = "Architect", Content = new string('x', 200000) } });
        foreach (var stage in new[] { RunStage.Analyzing, RunStage.Planning, RunStage.PreparingContext, RunStage.Implementing })
        {
            var context = await new ContextBuilder(store).BuildAsync(project, run with { CurrentStage = stage }, default);
            Assert.Contains("Current requirements and instructions", context);
            Assert.Contains("Do not publish without approval", context);
            Assert.True(context.Length < 10000);
            ContextBudget.Compact(context, Agent());
        }
    }
}
