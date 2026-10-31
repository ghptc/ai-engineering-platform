using AIEngineeringPlatform.Application.Estimation;
using AIEngineeringPlatform.Domain;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System.Text.Json;
namespace AIEngineeringPlatform.Infrastructure;

public sealed partial class MongoPlatformStore : IRequirementScopeStore
{
    private IMongoCollection<ScopeHead> ScopeHeads => database.GetCollection<ScopeHead>("requirement_scope_heads_v1");
    private IMongoCollection<ScopeSnapshot> ScopeSnapshots => database.GetCollection<ScopeSnapshot>("requirement_scope_snapshots_v1");
    public async Task<RequirementScope?> CurrentScopeAsync(string projectId, CancellationToken ct)
    {
        var head = await ScopeHeads.Find(x => x.Id == projectId).FirstOrDefaultAsync(ct);
        return head == null ? null : await SnapshotAsync(projectId, head.Revision, ct);
    }
    public async Task<RequirementScope?> ScopeAsync(string projectId, string revision, CancellationToken ct)
    {
        // Reachability checks read bounded metadata; full source data is loaded once for the selected revision.
        var head = await ScopeHeads.Find(x => x.Id == projectId).FirstOrDefaultAsync(ct);
        var cursor = head?.Revision;
        for (var i = 0; cursor != null && i < RequirementScope.MaximumVersions; i++)
        {
            if (cursor == revision) return await SnapshotAsync(projectId, revision, ct);
            cursor = (await VersionAsync(projectId, cursor, ct))?.ParentRevision;
        }
        return null;
    }
    private Task<ScopeVersion?> VersionAsync(string projectId, string revision, CancellationToken ct) => ScopeSnapshots.Find(x => x.Id == projectId + ":" + revision).Project(x => x.Summary).FirstOrDefaultAsync(ct)!;
    public async Task<ScopeVersionPage> ScopeHistoryAsync(string projectId, string? before, CancellationToken ct)
    {
        var head = await ScopeHeads.Find(x => x.Id == projectId).FirstOrDefaultAsync(ct);
        var cursor = head?.Revision; var found = before == null; var items = new List<ScopeVersion>();
        for (var i = 0; cursor != null && i < RequirementScope.MaximumVersions; i++)
        {
            var summary = await VersionAsync(projectId, cursor, ct) ?? throw new InvalidOperationException("Scope history metadata is missing; preserve records for reconciliation.");
            if (found) items.Add(summary);
            else if (cursor == before) found = true;
            cursor = summary.ParentRevision;
            if (items.Count == 20) return new(items, cursor == null ? null : items[^1].Revision);
        }
        if (!found) throw new KeyNotFoundException("Scope history cursor is not in this project.");
        return new(items, null);
    }
    private async Task<RequirementScope?> SnapshotAsync(string projectId, string revision, CancellationToken ct)
    {
        var snapshot = await ScopeSnapshots.Find(x => x.Id == projectId + ":" + revision).FirstOrDefaultAsync(ct);
        return snapshot == null ? null : JsonSerializer.Deserialize<RequirementScope>(snapshot.Data, Json);
    }
    public async Task<bool> AppendScopeAsync(RequirementScope scope, string? expectedRevision, CancellationToken ct)
    {
        if (scope.ParentRevision != expectedRevision || scope.Version is < 1 or > RequirementScope.MaximumVersions) throw new ArgumentException("Scope parent/version exceeds supported history bounds.");
        var head = await ScopeHeads.Find(x => x.Id == scope.ProjectId).FirstOrDefaultAsync(ct);
        if (head?.Revision != expectedRevision || scope.Version != (head?.Version ?? 0) + 1) return false;
        var snapshot = new ScopeSnapshot { Id = scope.ProjectId + ":" + scope.Revision, Data = JsonSerializer.Serialize(scope, Json), Summary = new(scope.Revision, scope.ParentRevision, scope.Version, scope.CreatedAt, scope.Reason, scope.Document.FileName, scope.Requirements.Length, scope.ReadyForEstimation) };
        try { await ScopeSnapshots.InsertOneAsync(snapshot, cancellationToken: ct); }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey) { /* Immutable candidate can be reused after interruption. */ }
        var next = new ScopeHead { Id = scope.ProjectId, Revision = scope.Revision, Version = scope.Version };
        if (head == null)
        {
            try { await ScopeHeads.InsertOneAsync(next, cancellationToken: ct); return true; }
            catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
        }
        return (await ScopeHeads.ReplaceOneAsync(x => x.Id == scope.ProjectId && x.Revision == expectedRevision && x.Version == head.Version, next, cancellationToken: ct)).MatchedCount == 1;
    }
    private sealed class ScopeHead { [BsonId] public string Id { get; set; } = ""; public string Revision { get; set; } = ""; public int Version { get; set; } }
    private sealed class ScopeSnapshot { [BsonId] public string Id { get; set; } = ""; public string Data { get; set; } = ""; public ScopeVersion Summary { get; set; } = null!; }
}
