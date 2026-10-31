using AIEngineeringPlatform.Application.RuntimeV2;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;
namespace AIEngineeringPlatform.Infrastructure;

public sealed class MongoRuntimeInvocationStore : IRuntimeInvocationStore
{
    private readonly IMongoCollection<BsonDocument> invocations;
    public MongoRuntimeInvocationStore(string connection, string database)
    {
        var settings = MongoClientSettings.FromConnectionString(connection);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5); settings.SocketTimeout = TimeSpan.FromSeconds(10);
        invocations = new MongoClient(settings).GetDatabase(database).GetCollection<BsonDocument>("runtime_invocations_v2");
    }
    public async Task<RuntimeInvocationStart> BeginAsync(RuntimeInvocationCheckpoint proposed, CancellationToken ct)
    {
        try
        {
            await invocations.InsertOneAsync(new() { ["_id"] = proposed.Id, ["Fingerprint"] = proposed.Fingerprint, ["Owner"] = proposed.Owner, ["Status"] = "Running", ["Data"] = JsonSerializer.Serialize(proposed) }, cancellationToken: ct);
            return new(true, proposed);
        }
        catch (MongoWriteException error) when (error.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            var existing = await GetAsync(proposed.Id, ct);
            if (existing.Fingerprint != proposed.Fingerprint || existing.ContextFrameId != proposed.ContextFrameId) throw new UnauthorizedAccessException("Runtime invocation identity cannot be reused for different task input.");
            return new(false, existing);
        }
    }
    public async Task<RuntimeInvocationCheckpoint> GetAsync(string id, CancellationToken ct)
    {
        var document = await invocations.Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefaultAsync(ct) ?? throw new KeyNotFoundException("Runtime invocation checkpoint missing.");
        var checkpoint = JsonSerializer.Deserialize<RuntimeInvocationCheckpoint>(document["Data"].AsString) ?? throw new InvalidOperationException("Invalid runtime checkpoint.");
        if (checkpoint.Version != 1 || checkpoint.Id != id || checkpoint.Deadline < checkpoint.StartedAt || checkpoint.Status is not ("Running" or "Completed" or "Failed" or "OutcomeUnknown")) throw new RuntimeReconciliationException("Runtime checkpoint contract is incompatible.");
        if (checkpoint.Status == "Running" && checkpoint.Deadline <= DateTimeOffset.UtcNow)
        {
            var unknown = checkpoint with { Status = "OutcomeUnknown", ErrorCategory = "InvocationDeadlineExpired" };
            var filter = Builders<BsonDocument>.Filter.Eq("_id", id) & Builders<BsonDocument>.Filter.Eq("Status", "Running");
            if ((await invocations.UpdateOneAsync(filter, Builders<BsonDocument>.Update.Set("Status", "OutcomeUnknown").Set("Data", JsonSerializer.Serialize(unknown)), cancellationToken: ct)).MatchedCount == 1) return unknown;
            return await GetAsync(id, ct);
        }
        return checkpoint;
    }
    public async Task CompleteAsync(string id, string owner, AIEngineeringPlatform.Application.ModelExecutionResult result, CancellationToken ct)
    {
        var current = await GetAsync(id, ct);
        if (current.Owner != owner) throw new UnauthorizedAccessException("Runtime checkpoint ownership changed.");
        var completed = current with { Status = "Completed", Result = result, ErrorCategory = null };
        var filter = Builders<BsonDocument>.Filter.Eq("_id", id) & Builders<BsonDocument>.Filter.Eq("Owner", owner) & Builders<BsonDocument>.Filter.Eq("Status", "Running");
        if ((await invocations.UpdateOneAsync(filter, Builders<BsonDocument>.Update.Set("Status", "Completed").Set("Data", JsonSerializer.Serialize(completed)), cancellationToken: ct)).MatchedCount != 1) throw new RuntimeReconciliationException("Runtime completion could not be checkpointed; reconcile the original invocation.");
    }
    public async Task FailAsync(string id, string owner, string category, CancellationToken ct)
    {
        var current = await GetAsync(id, ct);
        if (current.Owner != owner || current.Status != "Running") return;
        var status = category is "OperationCanceledException" or "TaskCanceledException" or "TimeoutException" or "ProviderException" ? "OutcomeUnknown" : "Failed";
        await invocations.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", id) & Builders<BsonDocument>.Filter.Eq("Owner", owner) & Builders<BsonDocument>.Filter.Eq("Status", "Running"), Builders<BsonDocument>.Update.Set("Status", status).Set("Data", JsonSerializer.Serialize(current with { Status = status, ErrorCategory = category })), cancellationToken: ct);
    }
}
