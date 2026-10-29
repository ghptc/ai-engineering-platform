using AIEngineeringPlatform.Domain;
using System.Text.Json;
namespace AIEngineeringPlatform.Application;

public static class RecordPresentation
{
    public static RecordEntry Preview(RecordEntry entry)
    {
        if (entry.Kind == "tool" && entry.Content.Length > 8000)
        {
            try
            {
                var result = JsonSerializer.Deserialize<ToolResult>(entry.Content)!;
                return entry with { Content = JsonSerializer.Serialize(result with { Output = result.Output[..Math.Min(2000, result.Output.Length)], Execution = null }), Metadata = new(entry.Metadata) { ["payloadPreview"] = "true", ["payloadCharacters"] = entry.Content.Length.ToString() } };
            }
            catch (JsonException) { }
        }
        return entry.Kind == "event" && entry.Content.Length > 8000
        ? entry with { Content = "Large diagnostic payload; open recorded details.", Metadata = new(entry.Metadata) { ["payloadPreview"] = "true", ["payloadCharacters"] = entry.Content.Length.ToString() } }
        : entry;
    }
}
