using AIEngineeringPlatform.Application.RuntimeV2;
using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;

namespace AIEngineeringPlatform.Infrastructure;

public sealed class MongoAgentDispatchStore : IAgentDispatchStore
{
    private readonly IMongoCollection<BsonDocument> tasks;
    private readonly IRuntimeInvocationStore invocations;
    private readonly SemaphoreSlim initialization = new(1);
    private bool initialized;
    private async Task InitializeAsync(CancellationToken ct)
    {
        await initialization.WaitAsync(ct);
        try
        {
            if (initialized) return;
            await tasks.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("Role").Ascending("Status").Ascending("CreatedAt")), cancellationToken: ct);
            initialized = true;
        }
        finally { initialization.Release(); }
    }
    public MongoAgentDispatchStore(string connection, string database)
    {
        var settings = MongoClientSettings.FromConnectionString(connection);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5); settings.SocketTimeout = TimeSpan.FromSeconds(10);
        invocations = new MongoRuntimeInvocationStore(connection, database);
        tasks = new MongoClient(settings).GetDatabase(database).GetCollection<BsonDocument>("agent_dispatch_v2");
    }
    public async Task<AgentDispatch?> GetAsync(string id, CancellationToken ct)
    {
        var row = await tasks.Find(Filter(id)).FirstOrDefaultAsync(ct);
        if (row == null) return null;
        var task = Read(row);
        if (task.Status is "Running" or "OutcomeUnknown")
        {
            RuntimeInvocationCheckpoint? checkpoint = null;
            try { checkpoint = await invocations.GetAsync(id, ct); }
            catch (KeyNotFoundException) { } // Claimed task may not have reached the provider boundary.
            if (checkpoint is { Status: "Completed", Result: not null })
            {
                if (checkpoint.ContextFrameId != task.Request.ContextFrameId || checkpoint.Fingerprint != RuntimeInvocationIdentity.Fingerprint(task.Request)) throw new RuntimeReconciliationException("Completed runtime evidence does not match the frozen dispatch input.");
                var completed = task with { Status = "Completed", Result = checkpoint.Result, Failure = null };
                var recovered = await tasks.UpdateOneAsync(Filter(id) & Builders<BsonDocument>.Filter.Eq("Status", task.Status), Update(completed), cancellationToken: ct);
                return recovered.MatchedCount == 1 ? completed : await GetAsync(id, ct);
            }
        }
        if (task.Status is "Scheduled" or "Running" && task.Deadline <= DateTimeOffset.UtcNow)
        {
            var expired = task with { Status = task.Status == "Running" ? "OutcomeUnknown" : "Expired", Failure = "DispatchDeadlineExpired" };
            var changed = await tasks.UpdateOneAsync(Filter(id) & Builders<BsonDocument>.Filter.Eq("Status", task.Status), Update(expired), cancellationToken: ct);
            return changed.MatchedCount == 1 ? expired : await GetAsync(id, ct);
        }
        return task;
    }
    public async Task<AgentDispatch> EnqueueAsync(AgentDispatch task, CancellationToken ct)
    {
        await InitializeAsync(ct);
        if (task.Status != "Scheduled" || task.Worker != null || task.Result != null || task.Deadline <= task.CreatedAt || task.Deadline - task.CreatedAt > TimeSpan.FromMinutes(6)) throw new ArgumentException("Invalid bounded dispatch.");
        EngineeringMessageContract.Parse(EngineeringMessageContract.Serialize(task.Request.Task), "Orchestrator", new HashSet<string> { task.Role.ToString() });
        if (task.Id != task.Request.Task.MessageId || task.Role != task.Request.Context.Definition.Role) throw new UnauthorizedAccessException("Invalid dispatch identity.");
        try
        {
            await tasks.InsertOneAsync(new() { ["_id"] = task.Id, ["Role"] = task.Role.ToString(), ["Status"] = task.Status, ["CreatedAt"] = task.CreatedAt.UtcDateTime, ["Deadline"] = task.Deadline.UtcDateTime, ["Worker"] = BsonNull.Value, ["Data"] = JsonSerializer.Serialize(task) }, cancellationToken: ct);
            return task;
        }
        catch (MongoWriteException error) when (error.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            var saved = await GetAsync(task.Id, ct) ?? throw new RuntimeReconciliationException("Dispatch identity missing after duplicate insertion.");
            if (JsonSerializer.Serialize(saved.Request) != JsonSerializer.Serialize(task.Request)) throw new RuntimeReconciliationException("Dispatch identity cannot replace its frozen input.");
            return saved;
        }
    }
    public async Task<AgentDispatch?> ClaimAsync(AgentRole role, string worker, CancellationToken ct)
    {
        await InitializeAsync(ct);
        if (string.IsNullOrWhiteSpace(worker) || worker.Length > 128) throw new ArgumentException("Worker identity is required.");
        // Read a candidate then CAS by identity/status; expired or Running tasks are never automatically retried.
        var row = await tasks.Find(Builders<BsonDocument>.Filter.Eq("Role", role.ToString()) & Builders<BsonDocument>.Filter.Eq("Status", "Scheduled") & Builders<BsonDocument>.Filter.Gt("Deadline", DateTime.UtcNow)).Sort(Builders<BsonDocument>.Sort.Ascending("CreatedAt")).FirstOrDefaultAsync(ct);
        if (row == null) return null;
        var task = Read(row) with { Status = "Running", Worker = worker };
        var claimed = await tasks.UpdateOneAsync(Filter(task.Id) & Builders<BsonDocument>.Filter.Eq("Status", "Scheduled"), Update(task), cancellationToken: ct);
        return claimed.MatchedCount == 1 ? task : null;
    }
    public async Task FinishAsync(string id, string worker, ModelExecutionResult? result, string? failure, CancellationToken ct)
    {
        var current = await GetAsync(id, ct) ?? throw new RuntimeReconciliationException("Dispatch checkpoint unavailable.");
        if (current.Status == "Completed" && (result == null || JsonSerializer.Serialize(current.Result) == JsonSerializer.Serialize(result))) return;
        if (current.Status != "Running" || current.Worker != worker) throw new RuntimeReconciliationException("Dispatch ownership changed; preserve original runtime outcome.");
        var finished = current with { Status = result == null ? "OutcomeUnknown" : "Completed", Result = result, Failure = failure };
        var match = Filter(id) & Builders<BsonDocument>.Filter.Eq("Status", "Running") & Builders<BsonDocument>.Filter.Eq("Worker", worker);
        if ((await tasks.UpdateOneAsync(match, Update(finished), cancellationToken: ct)).MatchedCount != 1) throw new RuntimeReconciliationException("Dispatch completion lost ownership; reconcile its runtime checkpoint.");
    }
    private static FilterDefinition<BsonDocument> Filter(string id) => Builders<BsonDocument>.Filter.Eq("_id", id);
    private static UpdateDefinition<BsonDocument> Update(AgentDispatch task) => Builders<BsonDocument>.Update.Set("Status", task.Status).Set("Worker", task.Worker == null ? (BsonValue)BsonNull.Value : new BsonString(task.Worker)).Set("Data", JsonSerializer.Serialize(task));
    private static AgentDispatch Read(BsonDocument row) => JsonSerializer.Deserialize<AgentDispatch>(row["Data"].AsString) ?? throw new RuntimeReconciliationException("Invalid dispatch checkpoint.");
}
