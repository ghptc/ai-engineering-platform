using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using System.Text.Json;
using Xunit;
namespace AIEngineeringPlatform.Application.Tests;
public sealed class BootstrapProgressTests
{
    [Fact] public void FailedRepeatedBootstrapCannotResetTheProgressGuardButNewSourceDoes()
    {
        var records = new List<RecordEntry> { new() { Kind = "tool", Sequence = 1, Name = "ReadRepository", Content = JsonSerializer.Serialize(new ToolResult(true, "Baseline", WorkspaceRevision: "before")) } };
        for (var i = 0; i < 14; i++)
        {
            var model = new ModelExecutionResult(new("Bootstrap attempt", "CONTINUE", [], [], [], [new("PrepareFrontendLock", JsonSerializer.SerializeToElement(new { }))], ""), "fixture-" + i, 1, 1, 0);
            records.Add(new() { Kind = "task", Sequence = i * 2 + 2, Content = JsonSerializer.Serialize(model) });
            records.Add(new() { Kind = "tool", Sequence = i * 2 + 3, Name = "PrepareFrontendLock", Content = JsonSerializer.Serialize(new ToolResult(false, "Existing lock preserved; reuse evidence")), Metadata = new() { ["argumentsHash"] = "same" } });
        }
        Assert.True(InspectionProgress.RepeatedTurns(records) >= InspectionProgress.StopAfter);
        records.Add(new() { Kind = "tool", Sequence = 100, Name = "PrepareFrontendLock", Content = JsonSerializer.Serialize(new ToolResult(true, "Created approved lock", WorkspaceRevision: "after")) });
        Assert.Equal(0, InspectionProgress.RepeatedTurns(records));
        Assert.Equal(6, InspectionProgress.FeedbackAfter); Assert.Equal(12, InspectionProgress.StopAfter);
    }
}
