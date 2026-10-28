using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using NSubstitute;
using Xunit;
namespace AIEngineeringPlatform.Application.Tests;

public class UseCaseTests
{
    private readonly IPlatformStore store = Substitute.For<IPlatformStore>();
    private readonly IExecutionPublisher publisher = Substitute.For<IExecutionPublisher>();
    [Fact] public async Task StartRequiresIdempotencyKey() => await Assert.ThrowsAsync<ArgumentException>(() => new PlatformService(store, publisher).StartAsync("project", "", default));
    [Fact]
    public async Task StartCopiesProjectPolicy()
    {
        store.GetProjectAsync("project", default).Returns(new Project { Id = "project", Name = "Test", ExecutionPolicy = new(MaxIterations: 3) });
        store.RecordsAsync("requirement", "project", null, null, 0, 0, 1000, default).Returns(Array.Empty<RecordEntry>());
        store.CreateRunAsync(Arg.Any<ExecutionRun>(), default).Returns(x => x.Arg<ExecutionRun>());
        var run = await new PlatformService(store, publisher).StartAsync("project", "unique", default);
        Assert.Equal(3, run.Policy.MaxIterations); Assert.Equal("unique", run.IdempotencyKey);
    }
    [Fact]
    public async Task ConcurrencyConflictIsSurfaced()
    {
        await Assert.ThrowsAsync<ConcurrencyException>(() => new PlatformService(store, publisher).SaveProjectAsync(new Project { Name = "Test" }, 0, default));
    }
    [Fact]
    public async Task MessagesPersistBeforeDelivery()
    {
        store.GetRecordAsync("conversation", "c", default).Returns(new RecordEntry { Id = "c", ProjectId = "p", Kind = "conversation" });
        await new PlatformService(store, publisher).MessageAsync("c", "Client", "Hello", default);
        Received.InOrder(() => { store.AppendAsync(Arg.Any<RecordEntry>(), default); publisher.PublishAsync(Arg.Any<RecordEntry>(), default); });
    }
    [Fact]
    public async Task NonPausedRunCannotResume()
    {
        store.GetRunAsync("r", default).Returns(new ExecutionRun { Id = "r", CurrentStage = RunStage.Analyzing });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlatformService(store, publisher).ControlAsync("r", "resume", default));
    }
    [Fact]
    public async Task ContextIncludesEvidenceAndUserContext()
    {
        store.RecordsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<int>(), default).Returns(Array.Empty<RecordEntry>());
        var text = await new ContextBuilder(store).BuildAsync(new Project { Name = "Project", OriginalContext = "Acceptance: save data" }, new ExecutionRun(), default);
        Assert.Contains("Acceptance: save data", text); Assert.Contains("verified tool evidence", text);
    }
    [Fact]
    public void PublishedReviewContainsActionableFindingsWithoutInventedInlinePositions()
    {
        var review = new AgentResult("Fix the failing contract.", "REQUEST_CHANGES", [], ["tests-1"], [], [], "", Findings: [new("High", "Feature.cs", 999, "Missing validation", "Validate the request before writing.")]);
        var markdown = ReviewMarkdown.Format(review);
        Assert.Contains("High", markdown); Assert.Contains("Feature.cs", markdown); Assert.Contains("Validate the request", markdown); Assert.DoesNotContain("999", markdown);
    }
}
