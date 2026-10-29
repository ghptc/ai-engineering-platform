using AIEngineeringPlatform.Domain;
namespace AIEngineeringPlatform.Application;

public static class RecordPresentation
{
    public static RecordEntry Preview(RecordEntry entry) => entry.Kind == "event" && entry.Content.Length > 8000
        ? entry with { Content = "Large diagnostic payload; open recorded details.", Metadata = new(entry.Metadata) { ["payloadPreview"] = "true", ["payloadCharacters"] = entry.Content.Length.ToString() } }
        : entry;
}
