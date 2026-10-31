using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using System.Text.Json;
using Xunit;

namespace AIEngineeringPlatform.Application.Tests;

public sealed class InspectionProgressTests
{
    [Theory]
    [InlineData(0, 12)]
    [InlineData(6, 6)]
    [InlineData(6, 1001)]
    public void InvalidThresholdsFailClosed(int feedback, int stop)
        => Assert.False(new InspectionProgressOptions { FeedbackAfterRepeatedTurns = feedback, StopAfterRepeatedTurns = stop }.IsValid);

    [Fact]
    public void RepeatedCachedRequestsAreBoundedAcrossCheckpointRecovery()
    {
        var records = Enumerable.Range(0, InspectionProgress.StopAfter + 1).Select(i => Task(i, "ReadFile", new { path = "backend/Feature.cs" })).ToArray();
        Assert.Equal(InspectionProgress.FeedbackAfter, InspectionProgress.RepeatedTurns(records.Take(InspectionProgress.FeedbackAfter + 1)));
        // Serialize and reload as a worker restart would; no process-local counter is needed.
        var restored = JsonSerializer.Deserialize<RecordEntry[]>(JsonSerializer.Serialize(records))!;
        Assert.Equal(InspectionProgress.StopAfter, InspectionProgress.RepeatedTurns(restored));
    }

    [Theory]
    [InlineData("WriteFile")]
    [InlineData("ApplyPatch")]
    [InlineData("RunTests")]
    public void ImplementationAndVerificationEndAnInspectionStreak(string operation)
    {
        var records = Enumerable.Range(0, 10).Select(i => Task(i, "ListFiles", new { })).Append(Task(10, operation, new { })).ToArray();
        Assert.Equal(0, InspectionProgress.RepeatedTurns(records));
    }

    [Fact]
    public void NewReadRangesDoNotCountAsRepeatsAndChangedWorkspaceResetsTheStreak()
    {
        var records = Enumerable.Range(0, 10).Select(i => Task(i, "ReadProjectMemory", new { kind = "tool", id = "diff", offset = 0, limit = 4000 })).ToList();
        records.Add(Task(10, "ReadProjectMemory", new { kind = "tool", id = "diff", offset = 4000, limit = 4000 }));
        Assert.Equal(9, InspectionProgress.RepeatedTurns(records));
        records = [Tool(0, "revision-1"), .. Enumerable.Range(1, 10).Select(i => Task(i, "ListFiles", new { })), Tool(11, "revision-2")];
        Assert.Equal(0, InspectionProgress.RepeatedTurns(records));
    }

    [Fact]
    public void NewReceiptIdsAreNotMistakenForNewInformation()
    {
        var records = new List<RecordEntry>();
        for (var i = 0; i < 14; i++)
        {
            records.Add(Task(i * 2, "ReadRepository", new { }));
            records.Add(Tool(i * 2 + 1, "revision"));
        }
        Assert.True(InspectionProgress.RepeatedTurns(records) >= InspectionProgress.StopAfter);
        records.Add(Tool(30, "revision", "changed repository facts"));
        Assert.Equal(0, InspectionProgress.RepeatedTurns(records));
    }

    private static RecordEntry Task(int sequence, string name, object args) => new()
    {
        Kind = "task", Sequence = sequence,
        Content = JsonSerializer.Serialize(new ModelExecutionResult(new("Inspect", "CONTINUE", [], [], [], [new(name, JsonSerializer.SerializeToElement(args))], ""), "response", 0, 0, 0))
    };
    private static RecordEntry Tool(int sequence, string revision, string output = "same repository facts") => new()
    {
        Kind = "tool", Name = "ReadRepository", Sequence = sequence,
        Content = JsonSerializer.Serialize(new ToolResult(true, output, "receipt-" + sequence, revision)),
        Metadata = new() { ["argumentsHash"] = "empty-args" }
    };
}
