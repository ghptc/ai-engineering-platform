using System.Text.Json;
using AIEngineeringPlatform.Domain;

namespace AIEngineeringPlatform.Application;

public sealed class InspectionProgressOptions
{
    public int FeedbackAfterRepeatedTurns { get; set; } = InspectionProgress.FeedbackAfter;
    public int StopAfterRepeatedTurns { get; set; } = InspectionProgress.StopAfter;
    public bool IsValid => FeedbackAfterRepeatedTurns >= 1 && StopAfterRepeatedTurns > FeedbackAfterRepeatedTurns && StopAfterRepeatedTurns <= 1000;
}

/// <summary>Rebuilds inspection-loop state from durable checkpoints, including reused tool requests.</summary>
public static class InspectionProgress
{
    public const int FeedbackAfter = 6;
    public const int StopAfter = 12;
    public const string Feedback = "Repeated inspection without new evidence detected. Use the returned evidence and retrieve only specific missing ranges. Continue the authorized acceptance criteria with a bounded implementation or verification step if justified; otherwise report a concrete blocker or evidence-backed no-op. Do not invent changes, repeat the same inspection cycle, bypass security, or claim missing tests passed.";

    public static int RepeatedTurns(IEnumerable<RecordEntry> records)
    {
        var seen = new HashSet<string>();
        var observations = new Dictionary<string, string>();
        string? revision = null;
        var repeated = 0;
        foreach (var record in records.OrderBy(x => x.Sequence))
        {
            if (record.Kind == "tool")
            {
                var result = JsonSerializer.Deserialize<ToolResult>(record.Content)!;
                if (result.Success && result.WorkspaceRevision is { } current)
                {
                    if (revision != null && revision != current) { seen.Clear(); observations.Clear(); repeated = 0; }
                    revision = current;
                }
                if (IsInspection(record.Name) && record.Metadata.TryGetValue("argumentsHash", out var hash))
                {
                    var key = record.Name + "/" + hash;
                    var observation = JsonSerializer.Serialize(new { result.Success, result.Output });
                    if (observations.TryGetValue(key, out var prior) && prior != observation) repeated = 0;
                    observations[key] = observation;
                }
                continue;
            }
            if (record.Kind != "task") continue;
            var calls = JsonSerializer.Deserialize<ModelExecutionResult>(record.Content)!.Output.ToolCalls;
            if (calls.Length == 0 || calls.Any(x => !IsInspection(x.Name)))
            {
                seen.Clear(); observations.Clear(); repeated = 0;
                continue;
            }
            var keys = calls.Select(x => x.Name + "/" + Canonical(x.Arguments)).ToArray();
            // A new page does not itself repeat work, but cannot erase earlier repeated turns.
            // Changed observations/revisions and implementation/verification checkpoints reset them.
            if (keys.All(seen.Contains)) repeated++;
            foreach (var key in keys) seen.Add(key);
        }
        return repeated;
    }

    private static bool IsInspection(string name) => name is "ReadRepository" or "ReconcileRepository" or "ListFiles" or "ReadFile" or "SearchCode" or "InspectDiff" or "ReadProjectMemory" or "ReadPullRequest";
    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => JsonSerializer.Serialize(x.Name) + ":" + Canonical(x.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]",
        _ => value.GetRawText()
    };
}
