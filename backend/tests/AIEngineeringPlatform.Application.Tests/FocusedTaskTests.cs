using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using NSubstitute;
using Xunit;
namespace AIEngineeringPlatform.Application.Tests;
public class FocusedTaskTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TaskReferencesCannotImportAnotherProjectsRequirementsOrHumanInstructions(bool human)
    {
        var store = Substitute.For<IPlatformStore>();
        store.GetProjectAsync("p", default).Returns(new Project { Id = "p", Name = "Project" });
        store.GetRecordAsync(human ? "message" : "requirement", "foreign", default).Returns(new RecordEntry { Id = "foreign", ProjectId = "another-project", Name = "Client" });
        var task = new EngineeringTask("Answer", "Change answer", ["Tests pass"], RequirementIds: human ? [] : ["foreign"], HumanInstructionIds: human ? ["foreign"] : []);
        await Assert.ThrowsAsync<ArgumentException>(() => new PlatformService(store, Substitute.For<IExecutionPublisher>()).StartTasksAsync("p", "idempotent-key", [task], default));
        await store.DidNotReceive().CreateRunAsync(Arg.Any<ExecutionRun>(), default);
    }
    [Fact]
    public async Task FocusedTaskDoesNotLoadConversationsOrProjectArtifactHistory()
    {
        var store = Substitute.For<IPlatformStore>();
        store.RecordsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<int>(), default).Returns(Array.Empty<RecordEntry>());
        var project = new Project { Id = "p", OriginalContext = "Historical knowledge " + new string('h', 100000), StandingInstructions = "Never modify main or expose credentials" };
        var run = new ExecutionRun { Id = "r", ProjectId = "p", Tasks = [new("Answer", "Change answer() from 1 to 2", ["Assert answer() equals 2", "Run node --test"])], CurrentStage = RunStage.Planning };
        var text = await new ContextBuilder(store).BuildAsync(project, run, default);
        Assert.Contains("Change answer() from 1 to 2", text); Assert.Contains("Never modify main", text); Assert.Contains("Assert answer() equals 2", text);
        Assert.DoesNotContain("Historical knowledge", text); Assert.True(ContextBudget.Tokens(text) < 5000);
        Assert.Contains("ProjectKnowledge", text); Assert.Contains("CurrentTask", text); Assert.Contains("ExecutionEvidence", text);
        await store.DidNotReceive().RecordsAsync("message", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<int>(), default);
        await store.DidNotReceive().RecordsAsync("artifact", "p", null, null, 0, 0, 1000, default);
    }
}
