using AIEngineeringPlatform.Domain;
using System.Text.Json;
namespace AIEngineeringPlatform.Application;

public sealed record RecordWindow(IReadOnlyList<RecordEntry> Items, long? NextBefore, bool HasMore);
public sealed record UsageGroup(string Key, long InputTokens, long OutputTokens, decimal EstimatedCostUsd, long Calls);
public sealed record UsageItem(string Id, DateTimeOffset CreatedAt, string ProjectId, string? RunId, string Agent, string Model, string ResponseId, long InputTokens, long OutputTokens, decimal EstimatedCostUsd);
public sealed record UsageLedger(UsageGroup Total, UsageGroup[] Projects, UsageGroup[] Executions, UsageGroup[] Agents, UsageGroup[] Models, UsageGroup[] Days, UsageItem[] Items, int Offset, bool HasMore, string CostBasis = "Estimated from configured model prices. Provider token counts are recorded when available; uncertain requests reserve their configured maximum cost; confirmed billing is unavailable.");
public sealed record EvidenceReference(string Id, string Name);
public sealed record WorkspaceDetails(RecordEntry Entry, RecordEntry? Tool, JsonElement? ToolInput, string InputAvailability, EvidenceReference[] Artifacts);
public interface IWorkspaceQueries
{
    Task<RecordWindow> WindowAsync(string kind, string? runId, string? conversationId, long? before, int limit, CancellationToken ct);
    Task<UsageLedger> UsageAsync(int offset, int limit, CancellationToken ct);
    Task<WorkspaceDetails> DetailsAsync(string runId, string recordId, string kind, CancellationToken ct);
}
