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
            // Add searchable scope indexes without modifying historical task/checkpoint payloads.
            while (true)
            {
                var legacy = await tasks.Find(Builders<BsonDocument>.Filter.Exists("CreatedTicks", false)).Limit(100).ToListAsync(ct);
                if (legacy.Count == 0) break;
                foreach (var row in legacy)
                {
                    var saved = Read(row);
                    await tasks.UpdateOneAsync(Filter(saved.Id), Builders<BsonDocument>.Update.Set("ProjectId", saved.Request.Task.ProjectId).Set("RunId", saved.Request.Task.RunId).Set("CreatedTicks", saved.CreatedAt.UtcTicks), cancellationToken: ct);
                }
            }
            await tasks.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("RunId").Descending("CreatedTicks").Descending("_id")), cancellationToken: ct);
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
            if (checkpoint != null && (checkpoint.ContextFrameId != task.Request.ContextFrameId || checkpoint.Fingerprint != RuntimeInvocationIdentity.Fingerprint(task.Request))) throw new RuntimeReconciliationException("Runtime evidence does not match the frozen dispatch input.");
            if (checkpoint is { Status: "Failed" or "OutcomeUnknown" })
            {
                if (task.Status == checkpoint.Status && task.Failure == checkpoint.ErrorCategory) return task;
                var failed = task with { Status = checkpoint.Status, Failure = checkpoint.ErrorCategory };
                var changed = await tasks.UpdateOneAsync(Filter(id) & Builders<BsonDocument>.Filter.Eq("Status", task.Status), Update(failed), cancellationToken: ct);
                return changed.MatchedCount == 1 ? failed : await GetAsync(id, ct);
            }
            if (checkpoint is { Status: "Completed", Result: not null })
            {
                var completed = task with { Status = "Completed", Result = checkpoint.Result, Failure = null, CompletedAt = checkpoint.CompletedAt };
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
            await tasks.InsertOneAsync(new() { ["_id"] = task.Id, ["Role"] = task.Role.ToString(), ["ProjectId"] = task.Request.Task.ProjectId, ["RunId"] = task.Request.Task.RunId, ["Status"] = task.Status, ["CreatedAt"] = task.CreatedAt.UtcDateTime, ["CreatedTicks"] = task.CreatedAt.UtcTicks, ["Deadline"] = task.Deadline.UtcDateTime, ["Worker"] = BsonNull.Value, ["Data"] = JsonSerializer.Serialize(task) }, cancellationToken: ct);
            return task;
        }
        catch (MongoWriteException error) when (error.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            var saved = await GetAsync(task.Id, ct) ?? throw new RuntimeReconciliationException("Dispatch identity missing after duplicate insertion.");
            if (JsonSerializer.Serialize(saved.Request) != JsonSerializer.Serialize(task.Request)) throw new RuntimeReconciliationException("Dispatch identity cannot replace its frozen input.");
            return saved;
        }
    }
    public Task<AgentDispatch?> ClaimAsync(AgentRole role, string worker, CancellationToken ct) => ClaimAsync(Builders<BsonDocument>.Filter.Eq("Role", role.ToString()), worker, ct);
    public Task<AgentDispatch?> ClaimNextAsync(string worker, CancellationToken ct) => ClaimAsync(Builders<BsonDocument>.Filter.In("Role", Enum.GetNames<AgentRole>()), worker, ct);
    private async Task<AgentDispatch?> ClaimAsync(FilterDefinition<BsonDocument> roles, string worker, CancellationToken ct)
    {
        await InitializeAsync(ct);
        if (string.IsNullOrWhiteSpace(worker) || worker.Length > 128) throw new ArgumentException("Worker identity is required.");
        // Read a candidate then CAS by identity/status; expired or Running tasks are never automatically retried.
        var row = await tasks.Find(roles & Builders<BsonDocument>.Filter.Eq("Status", "Scheduled") & Builders<BsonDocument>.Filter.Gt("Deadline", DateTime.UtcNow)).Sort(Builders<BsonDocument>.Sort.Ascending("CreatedAt")).FirstOrDefaultAsync(ct);
        if (row == null) return null;
        var task = Read(row) with { Status = "Running", Worker = worker, StartedAt = DateTimeOffset.UtcNow };
        var claimed = await tasks.UpdateOneAsync(Filter(task.Id) & Builders<BsonDocument>.Filter.Eq("Status", "Scheduled"), Update(task), cancellationToken: ct);
        return claimed.MatchedCount == 1 ? task : null;
    }
    public async Task FinishAsync(string id, string worker, ModelExecutionResult? result, string? failure, CancellationToken ct)
    {
        var current = await GetAsync(id, ct) ?? throw new RuntimeReconciliationException("Dispatch checkpoint unavailable.");
        if (current.Worker == worker && current.Status == "Completed" && (result == null || JsonSerializer.Serialize(current.Result) == JsonSerializer.Serialize(result))) return;
        if (current.Worker == worker && (current.Status is "Failed" or "OutcomeUnknown") && result == null) return;
        if (current.Status != "Running" || current.Worker != worker) throw new RuntimeReconciliationException("Dispatch ownership changed; preserve original runtime outcome.");
        var finished = current with { Status = result == null ? "OutcomeUnknown" : "Completed", Result = result, Failure = failure, CompletedAt = DateTimeOffset.UtcNow };
        var match = Filter(id) & Builders<BsonDocument>.Filter.Eq("Status", "Running") & Builders<BsonDocument>.Filter.Eq("Worker", worker);
        if ((await tasks.UpdateOneAsync(match, Update(finished), cancellationToken: ct)).MatchedCount != 1) throw new RuntimeReconciliationException("Dispatch completion lost ownership; reconcile its runtime checkpoint.");
    }
    public async Task<AgentTaskWindow> HistoryAsync(string projectId, string runId, string? before, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100 || before?.Length > 128) throw new ArgumentException("Invalid worker task history window.");
        await InitializeAsync(ct);
        var filter = Builders<BsonDocument>.Filter.Eq("ProjectId", projectId) & Builders<BsonDocument>.Filter.Eq("RunId", runId);
        if (before != null)
        {
            var cursor = await tasks.Find(filter & Filter(before)).FirstOrDefaultAsync(ct) ?? throw new KeyNotFoundException("Worker history cursor is unavailable in this execution.");
            filter &= Builders<BsonDocument>.Filter.Lt("CreatedTicks", cursor["CreatedTicks"]) | (Builders<BsonDocument>.Filter.Eq("CreatedTicks", cursor["CreatedTicks"]) & Builders<BsonDocument>.Filter.Lt("_id", before));
        }
        var rows = await tasks.Find(filter).Sort(Builders<BsonDocument>.Sort.Descending("CreatedTicks").Descending("_id")).Limit(limit + 1).ToListAsync(ct);
        var selected = new List<AgentDispatch>();
        foreach (var row in rows.Take(limit)) selected.Add(await GetAsync(row["_id"].AsString, ct) ?? throw new RuntimeReconciliationException("Worker history identity disappeared."));
        var items = selected.Select(task => new AgentTaskView(task.Id, task.Role.ToString(), task.Request.Task.Metadata.Stage, task.Status,
            Safe(task.Request.Task.Objective, 400), task.Request.Task.SourceRevision, task.Request.Task.Metadata.Attempt, task.CreatedAt, task.StartedAt, task.CompletedAt,
            task.Result == null ? null : Safe(task.Result.Output.Summary, 1000), task.Failure == null ? null : Safe(task.Failure, 200))).ToArray();
        return new(items, rows.Count > limit ? items.Last().Id : null, rows.Count > limit);
    }
    private static string Safe(string value, int length) { var safe = SecretRedactor.Redact(value); return safe.Length <= length ? safe : safe[..length] + " [bounded]"; }
    private static FilterDefinition<BsonDocument> Filter(string id) => Builders<BsonDocument>.Filter.Eq("_id", id);
    private static UpdateDefinition<BsonDocument> Update(AgentDispatch task) => Builders<BsonDocument>.Update.Set("Status", task.Status).Set("Worker", task.Worker == null ? (BsonValue)BsonNull.Value : new BsonString(task.Worker)).Set("Data", JsonSerializer.Serialize(task));
    private static AgentDispatch Read(BsonDocument row) => JsonSerializer.Deserialize<AgentDispatch>(row["Data"].AsString) ?? throw new RuntimeReconciliationException("Invalid dispatch checkpoint.");
}
