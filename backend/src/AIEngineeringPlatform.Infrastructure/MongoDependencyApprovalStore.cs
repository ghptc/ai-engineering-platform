using AIEngineeringPlatform.Application.Dependencies;
using AIEngineeringPlatform.Domain;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System.Text.Json;
namespace AIEngineeringPlatform.Infrastructure;
public sealed class MongoDependencyApprovalStore(string connection, string database) : IDependencyApprovalStore
{
    private readonly IMongoCollection<Document> collection = new MongoClient(connection).GetDatabase(database).GetCollection<Document>("dependency_approvals_v1");
    public async Task<DependencyApprovalRequest?> GetAsync(string id, CancellationToken ct) { var d = await collection.Find(x => x.Id == id).FirstOrDefaultAsync(ct); return d == null ? null : JsonSerializer.Deserialize<DependencyApprovalRequest>(d.Data); }
    public async Task<IReadOnlyList<DependencyRequestSummary>> ListAsync(string? projectId, string? runId, CancellationToken ct)
    {
        var filter = Builders<Document>.Filter.Empty;
        if (projectId != null) filter &= Builders<Document>.Filter.Eq(x => x.ProjectId, projectId);
        if (runId != null) filter &= Builders<Document>.Filter.Eq(x => x.RunId, runId);
        return (await collection.Find(filter).SortByDescending(x => x.Waiting).ThenByDescending(x => x.CreatedAt).Limit(100).Project(x => x.Summary).ToListAsync(ct)).ToArray();
    }
    public async Task<bool> SaveAsync(DependencyApprovalRequest request, long? expectedVersion, CancellationToken ct)
    {
        var doc = new Document { Id = request.Id, ProjectId = request.ProjectId, RunId = request.RunId, Version = request.Version, CreatedAt = request.CreatedAt.UtcDateTime, Data = JsonSerializer.Serialize(request), Summary = DependencyRequestSummary.From(request), Waiting = request.Status is DependencyApprovalStatus.Pending or DependencyApprovalStatus.Resolving or DependencyApprovalStatus.ReadyForReview };
        if (expectedVersion == null) { try { await collection.InsertOneAsync(doc, cancellationToken: ct); return true; } catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; } }
        return (await collection.ReplaceOneAsync(x => x.Id == request.Id && x.Version == expectedVersion, doc, cancellationToken: ct)).MatchedCount == 1;
    }
    private sealed class Document { [BsonId] public string Id { get; set; } = ""; public string ProjectId { get; set; } = ""; public string RunId { get; set; } = ""; public long Version { get; set; } public DateTime CreatedAt { get; set; } public string Data { get; set; } = ""; public DependencyRequestSummary Summary { get; set; } = null!; public bool Waiting { get; set; } }
}
