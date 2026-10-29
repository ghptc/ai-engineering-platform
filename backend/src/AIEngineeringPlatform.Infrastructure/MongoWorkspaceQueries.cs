using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using MongoDB.Driver;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace AIEngineeringPlatform.Infrastructure;

public sealed partial class MongoPlatformStore
{
    public async Task<RecordWindow> WindowAsync(string kind, string? runId, string? conversationId, long? before, int limit, CancellationToken ct)
    {
        var f = Builders<EntryDocument>.Filter.Eq(x => x.Kind, kind);
        if (runId != null) f &= Builders<EntryDocument>.Filter.Eq(x => x.RunId, runId);
        if (conversationId != null) f &= Builders<EntryDocument>.Filter.Eq(x => x.ConversationId, conversationId);
        if (before != null) f &= Builders<EntryDocument>.Filter.Lt(x => x.Sequence, before.Value);
        limit = Math.Clamp(limit, 1, 100);
        var found = (await entries.Find(f).SortByDescending(x => x.Sequence).Limit(limit + 1).ToListAsync(ct)).Select(x => Decode<RecordEntry>(x.Data)! with { Status = x.Status }).ToArray();
        var page = found.Take(limit).Select(x => x with { Content = SecretRedactor.Redact(x.Content) }).Select(RecordPresentation.Preview).ToArray();
        if (kind == "review") page = page.Select(record =>
        {
            for (var iteration=1;iteration<=20;iteration++)
            {
                string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
                if (record.Id != Hash($"{record.RunId}/review/{iteration}") && record.Id != Hash($"{record.RunId}/pr-review/{iteration}")) continue;
                return record with { Metadata = new(record.Metadata) { ["iteration"] = iteration.ToString(), ["diffArtifactId"] = Hash($"{record.RunId}/Reviewing/{iteration}/artifact/actual-diff"), ["testArtifactId"] = Hash($"{record.RunId}/RunningTests/{iteration}/artifact/test-report"), ["prArtifactId"] = Hash($"{record.RunId}/PreparingDelivery/{iteration}/artifact/pull-request") } };
            }
            return record;
        }).ToArray();
        if (kind == "message" && runId != null)
        {
            var selections = await entries.Find(x => x.RunId == runId && x.Kind == "event" && x.Name == "AgentsSelected").SortBy(x => x.Sequence).Limit(100).ToListAsync(ct);
            page = page.Select(record =>
            {
                var selection = selections.LastOrDefault(x => x.Sequence < record.Sequence);
                if (selection == null) return record;
                using var payload = JsonDocument.Parse(Decode<RecordEntry>(selection.Data)!.Content);
                return payload.RootElement.TryGetProperty("TaskIndex", out var taskIndex) ? record with { Metadata = new(record.Metadata) { ["taskIndex"] = taskIndex.GetInt32().ToString() } } : record;
            }).ToArray();
        }
        return new(page, page.LastOrDefault()?.Sequence, found.Length > limit);
    }
    public async Task<UsageLedger> UsageAsync(int offset, int limit, CancellationToken ct)
    {
        var history = new List<UsageItem>();
        var totals = new Dictionary<string, (long Input, long Output, decimal Cost, long Calls)>();
        void Add(string key, ModelExecutionResult value)
        {
            totals.TryGetValue(key, out var old);
            totals[key] = (old.Input + value.InputTokens, old.Output + value.OutputTokens, old.Cost + value.Cost, old.Calls + 1);
        }
        // Provider envelopes are small metadata records; keep large task payloads streamed.
        var requests = (await entries.Find(x => x.Kind == "providerRequest").SortByDescending(x => x.Sequence).ToListAsync(ct)).Select(x => Decode<RecordEntry>(x.Data)!).ToArray();
        var requestResults = requests.GroupBy(x => x.Metadata["requestKey"]).Select(g => g.OrderBy(x => x.Status == "Reserved" ? 0 : 1).Last()).OrderByDescending(x => x.Sequence).ToArray();
        var requestIds = requestResults.Select(x => JsonSerializer.Deserialize<ProviderCharge>(x.Content, Json)!.Diagnostics.ResponseId).Where(x => x != null).ToHashSet();
        var index = 0;
        var requestIndex = 0;
        using var cursor = await entries.Find(x => x.Kind == "task" || x.Kind == "usage").SortByDescending(x => x.Sequence).ToCursorAsync(ct);
        while (await cursor.MoveNextAsync(ct)) foreach (var doc in cursor.Current)
        {
            while (requestIndex < requestResults.Length && requestResults[requestIndex].Sequence > doc.Sequence) Include(requestResults[requestIndex++]);
            Include(Decode<RecordEntry>(doc.Data)!);
        }
        while (requestIndex < requestResults.Length) Include(requestResults[requestIndex++]);
        void Include(RecordEntry record)
        {
            ModelExecutionResult result;
            if (record.Kind == "providerRequest")
            {
                var charge = JsonSerializer.Deserialize<ProviderCharge>(record.Content, Json)!;
                result = new(null!, charge.Diagnostics.ResponseId ?? "Unknown", charge.Diagnostics.InputTokens ?? 0, charge.Diagnostics.OutputTokens ?? 0, charge.Cost, charge.Diagnostics);
            }
            else
            {
                result = JsonSerializer.Deserialize<ModelExecutionResult>(record.Content, Json) ?? throw new InvalidOperationException("Persisted model usage is invalid.");
                if (result.Diagnostics != null && requestIds.Contains(result.ResponseId)) return;
            }
            var agent = record.Kind == "usage" ? "BusinessAnalyst (conversation)" : record.Name;
            var model = result.Diagnostics?.Model ?? (record.Kind == "usage" ? record.Name : record.Metadata.GetValueOrDefault("model") ?? "Unavailable (legacy record)");
            Add("total:", result); Add("project:" + record.ProjectId, result); Add("execution:" + (record.RunId ?? "Conversation"), result); Add("agent:" + agent, result); Add("model:" + model, result); Add("day:" + record.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd"), result);
            if (index >= offset && history.Count <= limit) history.Add(new(record.Id, record.CreatedAt, record.ProjectId, record.RunId, agent, model, result.ResponseId, result.InputTokens, result.OutputTokens, result.Cost));
            index++;
        }
        UsageGroup[] Groups(string prefix) => totals.Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(x => new UsageGroup(x.Key[prefix.Length..], x.Value.Input, x.Value.Output, x.Value.Cost, x.Value.Calls)).OrderByDescending(x => x.EstimatedCostUsd).ToArray();
        return new(Groups("total:").FirstOrDefault() ?? new("", 0, 0, 0, 0), Groups("project:"), Groups("execution:"), Groups("agent:"), Groups("model:"), Groups("day:").OrderBy(x => x.Key).ToArray(), history.Take(limit).ToArray(), offset, history.Count > limit);
    }
    public async Task<WorkspaceDetails> DetailsAsync(string runId, string recordId, string kind, CancellationToken ct)
    {
        var entry = await GetRecordAsync(kind, recordId, ct) ?? throw new KeyNotFoundException("Record not found.");
        if (entry.RunId != runId) throw new KeyNotFoundException("Record does not belong to this execution.");
        RecordEntry? tool = kind == "tool" ? entry : null;
        if (kind == "event" && entry.Name == "ToolInvocationCompleted")
        {
            using var payload = JsonDocument.Parse(entry.Content);
            var name = payload.RootElement.GetProperty("name").GetString();
            var expected = payload.RootElement.TryGetProperty("EvidenceId", out var eid) ? eid.GetString() : null;
            var docs = await entries.Find(x => x.RunId == runId && x.Kind == "tool" && x.Name == name && x.Sequence < entry.Sequence).SortByDescending(x => x.Sequence).Limit(10).ToListAsync(ct);
            tool = docs.Select(x => Decode<RecordEntry>(x.Data)!).FirstOrDefault(x => expected == null || JsonSerializer.Deserialize<ToolResult>(x.Content, Json)?.EvidenceId == expected);
        }
        JsonElement? input = null;
        if (tool != null)
        {
            var tasks = await entries.Find(x => x.RunId == runId && x.Kind == "task" && x.Sequence < tool.Sequence).SortByDescending(x => x.Sequence).Limit(200).ToListAsync(ct);
            foreach (var doc in tasks)
            {
                var task = Decode<RecordEntry>(doc.Data)!;
                var calls = JsonSerializer.Deserialize<ModelExecutionResult>(task.Content, Json)?.Output.ToolCalls ?? [];
                for (var i = 0; i < calls.Length; i++)
                {
                    var key = $"{runId}/{tool.Metadata.GetValueOrDefault("stage")}/{tool.Metadata.GetValueOrDefault("iteration")}/{task.Id}/tool/{i}";
                    if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant() == tool.Id)
                        input = JsonSerializer.Deserialize<JsonElement>(SecretRedactor.Redact(calls[i].Arguments.GetRawText()));
                }
                if (input != null) break;
            }
        }
        var artifacts = await entries.Find(x => x.RunId == runId && x.Kind == "artifact").SortByDescending(x => x.Sequence).Limit(100).ToListAsync(ct);
        // Redact at the read boundary as well as at the original tool persistence boundary.
        return new(entry with { Content = SecretRedactor.Redact(entry.Content) }, tool == null ? null : tool with { Content = SecretRedactor.Redact(tool.Content) }, input, input != null ? "Recorded model tool arguments" : "Input not recorded for this event or system-issued operation", artifacts.Select(x => new EvidenceReference(x.Id, x.Name)).ToArray());
    }
}
