using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json;
namespace AIEngineeringPlatform.Infrastructure;

public sealed partial class MongoPlatformStore : IPlatformStore, IWorkspaceQueries
{
    private readonly IMongoDatabase database;
    private readonly IMongoCollection<ProjectDocument> projects;
    private readonly IMongoCollection<RunDocument> runs;
    private readonly IMongoCollection<EntryDocument> entries;
    private readonly IMongoCollection<AgentDocument> agents;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public MongoPlatformStore(string connection, string name)
    {
        var settings = MongoClientSettings.FromConnectionString(connection);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        database = new MongoClient(settings).GetDatabase(name);
        projects = database.GetCollection<ProjectDocument>("projects"); runs = database.GetCollection<RunDocument>("runs");
        entries = database.GetCollection<EntryDocument>("records"); agents = database.GetCollection<AgentDocument>("agents");
    }
    public async Task InitializeAsync(CancellationToken ct)
    {
        await runs.Indexes.CreateManyAsync([
            new(new BsonDocument { { "ProjectId", 1 }, { "Key", 1 } }, new() { Unique = true, Name = "run_idempotency" }),
            new(new BsonDocument("ProjectId", 1), new CreateIndexOptions<RunDocument>() { Unique = true, PartialFilterExpression = new BsonDocument("Active", true), Name = "one_active_run" }),
            new(new BsonDocument { { "Runnable", 1 }, { "LeaseExpiry", 1 } })], ct);
        await entries.Indexes.CreateManyAsync([
            new(new BsonDocument("Sequence", 1), new CreateIndexOptions<EntryDocument> { Name = "operational_sequence" }),
            new(new BsonDocument { { "Kind", 1 }, { "ProjectId", 1 }, { "Sequence", 1 } }),
            new(new BsonDocument { { "RunId", 1 }, { "Kind", 1 }, { "Sequence", 1 } }),
            new(new BsonDocument { { "ConversationId", 1 }, { "Sequence", 1 } }),
            new(new BsonDocument("ExternalId", 1), new CreateIndexOptions<EntryDocument>() { Unique = true, PartialFilterExpression = new BsonDocument("ExternalId", new BsonDocument("$type", "string")) }),
            new(new BsonDocument { { "Status", 1 }, { "CreatedAt", -1 } })], ct);
        await projects.Indexes.CreateOneAsync(new CreateIndexModel<ProjectDocument>(new BsonDocument("UpdatedAt", -1)), cancellationToken: ct);
    }
    public async Task<Project?> GetProjectAsync(string id, CancellationToken ct) => Decode<Project>((await projects.Find(x => x.Id == id).FirstOrDefaultAsync(ct))?.Data);
    public async Task<IReadOnlyList<Project>> ProjectsAsync(int offset, int limit, CancellationToken ct) => (await projects.Find(FilterDefinition<ProjectDocument>.Empty).SortByDescending(x => x.UpdatedAt).Skip(offset).Limit(limit).ToListAsync(ct)).Select(x => Decode<Project>(x.Data)!).ToArray();
    public async Task<bool> SaveProjectAsync(Project project, long? expectedVersion, CancellationToken ct)
    {
        var doc = new ProjectDocument { Id = project.Id, Data = Encode(project), Version = project.Version, UpdatedAt = project.UpdatedAt.UtcDateTime, Status = project.Status };
        if (expectedVersion is null) { try { await projects.InsertOneAsync(doc, cancellationToken: ct); return true; } catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; } }
        return (await projects.ReplaceOneAsync(x => x.Id == project.Id && x.Version == expectedVersion, doc, cancellationToken: ct)).MatchedCount == 1;
    }
    private static RunDocument Document(ExecutionRun run) => new() { Id = run.Id, ProjectId = run.ProjectId, Key = run.IdempotencyKey, Data = Encode(run), Version = run.Version, Active = run.CurrentStage is not (RunStage.Completed or RunStage.Cancelled), Runnable = run.IsRunnable, LeaseExpiry = run.LeaseExpiry.UtcDateTime, Owner = run.LeaseOwner, Stage = run.Status, CreatedAt = run.StartedAt.UtcDateTime, Cost = run.Cost, TokenUsage = run.TokenUsage };
    private static ExecutionRun DecodeRun(RunDocument doc)
    {
        var run = Decode<ExecutionRun>(doc.Data)!;
        return run with { FailureReason = run.FailureReason is { } failure ? SecretRedactor.Redact(failure) : null, Version = doc.Version, LeaseOwner = doc.Owner, LeaseExpiry = new DateTimeOffset(DateTime.SpecifyKind(doc.LeaseExpiry, DateTimeKind.Utc)) };
    }
    public async Task<ExecutionRun?> GetRunAsync(string id, CancellationToken ct) { var doc = await runs.Find(x => x.Id == id).FirstOrDefaultAsync(ct); return doc is null ? null : DecodeRun(doc); }
    public async Task<IReadOnlyList<ExecutionRun>> RunsAsync(string? projectId, int offset, int limit, CancellationToken ct, string? status = null)
    {
        var filter = projectId == null ? FilterDefinition<RunDocument>.Empty : Builders<RunDocument>.Filter.Eq(x => x.ProjectId, projectId);
        if (status != null) filter &= Builders<RunDocument>.Filter.Eq(x => x.Stage, status);
        return (await runs.Find(filter).SortByDescending(x => x.CreatedAt).Skip(offset).Limit(limit).ToListAsync(ct)).Select(DecodeRun).ToArray();
    }
    public async Task<ExecutionRun> CreateRunAsync(ExecutionRun run, CancellationToken ct)
    {
        var existing = await runs.Find(x => x.ProjectId == run.ProjectId && x.Key == run.IdempotencyKey).FirstOrDefaultAsync(ct);
        if (existing is not null) return DecodeRun(existing);
        try { var doc = Document(run); doc.Outbox = [TransitionEvent(run, "RunQueued", "Execution queued.")]; await runs.InsertOneAsync(doc, cancellationToken: ct); return run; }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            existing = await runs.Find(x => x.ProjectId == run.ProjectId && x.Key == run.IdempotencyKey).FirstOrDefaultAsync(ct);
            if (existing is not null) return DecodeRun(existing);
            throw new InvalidOperationException("Project already has an active or recoverable execution.");
        }
    }
    public async Task<ExecutionRun?> ClaimAsync(string owner, TimeSpan lease, CancellationToken ct)
    {
        var doc = await runs.FindOneAndUpdateAsync(x => x.Runnable && x.LeaseExpiry < DateTime.UtcNow,
            Builders<RunDocument>.Update.Set(x => x.Owner, owner).Set(x => x.LeaseExpiry, DateTime.UtcNow.Add(lease)).Inc(x => x.Version, 1),
            new() { ReturnDocument = ReturnDocument.After }, ct);
        return doc is null ? null : DecodeRun(doc);
    }
    public async Task<bool> SaveRunAsync(ExecutionRun run, long expectedVersion, string? owner, CancellationToken ct)
    {
        var filter = Builders<RunDocument>.Filter.Where(x => x.Id == run.Id && x.Version == expectedVersion);
        if (owner != null) filter &= Builders<RunDocument>.Filter.Where(x => x.Owner == owner && x.LeaseExpiry > DateTime.UtcNow);
        var previous = await runs.Find(filter).FirstOrDefaultAsync(ct);
        if (previous == null) return false;
        var replacement = Document(run with { Version = expectedVersion + 1 });
        replacement.Outbox = previous.Outbox;
        if (previous.Stage != run.Status)
        {
            replacement.Outbox.Add(TransitionEvent(run with { Version = expectedVersion + 1 }, "StageChanged", previous.Stage + " → " + run.Status));
            if (run.CurrentStage == RunStage.Implementing && run.ReworkPlan.Any(x => x.Iteration == run.Iteration)) replacement.Outbox.Add(TransitionEvent(run with { Version = expectedVersion + 1 }, "ReworkPlanned", JsonSerializer.Serialize(run.ReworkPlan)));
            if (run.CurrentStage is RunStage.Completed or RunStage.Failed) replacement.Outbox.Add(TransitionEvent(run with { Version = expectedVersion + 1 }, run.CurrentStage == RunStage.Completed ? "RunCompleted" : "RunFailed", run.FailureReason ?? run.Status));
        }
        if (DecodeRun(previous).LimitExtensions.Length < run.LimitExtensions.Length)
            replacement.Outbox.Add(TransitionEvent(run with { Version = expectedVersion + 1 }, "LimitsExtended", JsonSerializer.Serialize(run.LimitExtensions.Last())));
        return (await runs.ReplaceOneAsync(filter, replacement, cancellationToken: ct)).MatchedCount == 1;
    }
    public async Task<bool> RenewLeaseAsync(string id, string owner, CancellationToken ct) => (await runs.UpdateOneAsync(x => x.Id == id && x.Owner == owner && x.LeaseExpiry > DateTime.UtcNow, Builders<RunDocument>.Update.Set(x => x.LeaseExpiry, DateTime.UtcNow.AddMinutes(2)), cancellationToken: ct)).MatchedCount == 1;
    public Task ReleaseLeaseAsync(string id, string owner, CancellationToken ct) => runs.UpdateOneAsync(x => x.Id == id && x.Owner == owner, Builders<RunDocument>.Update.Set(x => x.Owner, null).Set(x => x.LeaseExpiry, DateTime.UnixEpoch), cancellationToken: ct);
    public async Task AppendAsync(RecordEntry entry, CancellationToken ct) => await AppendCoreAsync(entry, ct);
    private async Task<bool> AppendCoreAsync(RecordEntry entry, CancellationToken ct)
    {
        var counters = database.GetCollection<BsonDocument>("counters");
        var counter = await counters.FindOneAndUpdateAsync(new BsonDocument("_id", "events"), new BsonDocument("$inc", new BsonDocument("value", 1L)), new() { IsUpsert = true, ReturnDocument = ReturnDocument.After }, ct);
        entry = entry with { Sequence = counter["value"].ToInt64() };
        try { await entries.InsertOneAsync(new() { Id = entry.Id, Kind = entry.Kind, Name = entry.Name, ProjectId = entry.ProjectId, RunId = entry.RunId, ConversationId = entry.ConversationId, ExternalId = entry.ExternalId, Status = entry.Status, Cost = entry.Kind == "usage" ? Decode<ModelExecutionResult>(entry.Content)!.Cost : 0, TokenUsage = entry.Kind == "usage" ? Decode<ModelExecutionResult>(entry.Content)!.InputTokens + Decode<ModelExecutionResult>(entry.Content)!.OutputTokens : 0, Sequence = entry.Sequence, CreatedAt = entry.CreatedAt.UtcDateTime, Data = Encode(entry) }, cancellationToken: ct); return true; }
        catch (MongoWriteException e)
        {
            if (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                var existing = await entries.Find(x => x.Id == entry.Id).FirstOrDefaultAsync(ct);
                if (existing != null && SameAppend(existing.Data, entry)) return false;
                throw new PersistenceFailureException(e.WriteError.Code, AppendIndex(e), entry.Id, entry.Kind, true);
            }
            throw new PersistenceFailureException(e.WriteError.Code, "not-applicable", entry.Id, entry.Kind, false);
        }
    }
    private static string AppendIndex(MongoWriteException error)
    {
        // Only known index labels are copied; the driver message can include the rejected value/document.
        if (error.WriteError.Message.Contains("index: _id_ ", StringComparison.Ordinal)) return "_id_";
        if (error.WriteError.Message.Contains("index: ExternalId_1 ", StringComparison.Ordinal)) return "ExternalId_1";
        return "unknown";
    }
    private static bool SameAppend(string existing, RecordEntry entry)
    {
        var saved = System.Text.Json.Nodes.JsonNode.Parse(existing)!.AsObject();
        var proposed = System.Text.Json.Nodes.JsonNode.Parse(Encode(entry))!.AsObject();
        // An append retry has a new local clock and allocated sequence, but the immutable event is unchanged.
        foreach (var field in new[] { "CreatedAt", "Sequence" }) { saved.Remove(field); proposed.Remove(field); }
        return System.Text.Json.Nodes.JsonNode.DeepEquals(saved, proposed);
    }
    public async Task<IReadOnlyList<RecordEntry>> RecordsAsync(string kind, string? projectId, string? runId, string? conversationId, long after, int offset, int limit, CancellationToken ct, string? status = null)
    {
        var f = Builders<EntryDocument>.Filter.Eq(x => x.Kind, kind) & Builders<EntryDocument>.Filter.Gt(x => x.Sequence, after);
        if (status != null) f &= Builders<EntryDocument>.Filter.Eq(x => x.Status, status);
        if (projectId != null) f &= Builders<EntryDocument>.Filter.Eq(x => x.ProjectId, projectId);
        if (runId != null) f &= Builders<EntryDocument>.Filter.Eq(x => x.RunId, runId);
        if (conversationId != null) f &= Builders<EntryDocument>.Filter.Eq(x => x.ConversationId, conversationId);
        return (await entries.Find(f).SortBy(x => x.Sequence).Skip(offset).Limit(limit).ToListAsync(ct)).Select(x => Decode<RecordEntry>(x.Data)! with { Status = x.Status }).ToArray();
    }
    public async Task<IReadOnlyList<RecordEntry>> OperationalRecordsAsync(long after, int limit, bool newestFirst, CancellationToken ct)
    {
        if (after < 0 || limit is < 1 or > 100) throw new ArgumentException("Invalid operational window.");
        var filter = Builders<EntryDocument>.Filter.In(x => x.Kind, new[] { "event", "message", "tool", "handoff", "approval", "artifact" }) & Builders<EntryDocument>.Filter.Gt(x => x.Sequence, after);
        var query = entries.Find(filter);
        query = newestFirst ? query.SortByDescending(x => x.Sequence) : query.SortBy(x => x.Sequence);
        return (await query.Limit(limit).ToListAsync(ct)).Select(x => Decode<RecordEntry>(x.Data)! with { Status = x.Status }).ToArray();
    }
    public async Task<RecordEntry?> GetRecordAsync(string kind, string id, CancellationToken ct)
    {
        var entry = await entries.Find(x => x.Id == id && x.Kind == kind).FirstOrDefaultAsync(ct);
        return entry == null ? null : Decode<RecordEntry>(entry.Data)! with { Status = entry.Status };
    }
    public async Task<bool> DecideApprovalAsync(string id, bool approve, string reason, CancellationToken ct)
    {
        var approval = await GetRecordAsync("approval", id, ct);
        if (approval == null) return false;
        approval = approval with { Status = approve ? "Approved" : "Rejected", Metadata = new() { ["decidedBy"] = "owner", ["decidedAt"] = DateTimeOffset.UtcNow.ToString("O"), ["reason"] = reason } };
        return (await entries.UpdateOneAsync(x => x.Id == id && x.Status == "Pending", Builders<EntryDocument>.Update.Set(x => x.Status, approval.Status).Set(x => x.Data, Encode(approval)), cancellationToken: ct)).ModifiedCount == 1;
    }
    public async Task MarkRequirementsVerifiedAsync(string[] ids, string runId, string evidenceId, CancellationToken ct)
    {
        foreach (var id in ids)
        {
            var entry = await GetRecordAsync("requirement", id, ct) ?? throw new KeyNotFoundException("Requirement not found.");
            entry = entry with { Status = "Verified", RunId = runId, Metadata = new(entry.Metadata) { ["verifiedRunId"] = runId, ["testEvidenceId"] = evidenceId } };
            await entries.UpdateOneAsync(x => x.Id == id && x.Kind == "requirement", Builders<EntryDocument>.Update.Set(x => x.Status, "Verified").Set(x => x.Data, Encode(entry)).Set(x => x.RunId, runId), cancellationToken: ct);
        }
    }
    public async Task<bool> IngestWebhookAsync(RecordEntry entry, CancellationToken ct)
    {
        try { return await AppendCoreAsync(entry, ct); }
        catch (PersistenceFailureException exception) when (exception.Code == 11000) { return false; }
    }
    public Task CompleteWebhookAsync(string id, string status, CancellationToken ct) => entries.UpdateOneAsync(x => x.Id == id && x.Kind == "webhook", Builders<EntryDocument>.Update.Set(x => x.Status, status), cancellationToken: ct);
    public async Task SaveAgentAsync(AgentDefinition agent, CancellationToken ct) => await agents.ReplaceOneAsync(x => x.Id == agent.Id, new() { Id = agent.Id, Data = Encode(agent) }, new ReplaceOptions { IsUpsert = true }, ct);
    public async Task<IReadOnlyList<AgentDefinition>> AgentsAsync(CancellationToken ct) => (await agents.Find(FilterDefinition<AgentDocument>.Empty).ToListAsync(ct)).Select(x => Decode<AgentDefinition>(x.Data)!).ToArray();
    private static string TransitionEvent(ExecutionRun run, string name, string content) => Encode(new RecordEntry { Id = $"transition-{run.Id}-{run.Version}-{name}", Kind = "event", ProjectId = run.ProjectId, RunId = run.Id, Name = name, Content = content, Metadata = new() { ["runVersion"] = run.Version.ToString(), ["durableTransition"] = "true" } });
    public async Task<IReadOnlyList<RecordEntry>> FlushOutboxAsync(CancellationToken ct)
    {
        var documents = await runs.Find(Builders<RunDocument>.Filter.SizeGt(x => x.Outbox, 0)).Limit(100).ToListAsync(ct);
        var published = new List<RecordEntry>();
        foreach (var document in documents) foreach (var value in document.Outbox)
            {
                var entry = Decode<RecordEntry>(value)!;
                await AppendAsync(entry, ct);
                published.Add((await GetRecordAsync("event", entry.Id, ct))!);
                await runs.UpdateOneAsync(x => x.Id == document.Id, Builders<RunDocument>.Update.Pull(x => x.Outbox, value), cancellationToken: ct);
            }
        return published;
    }
    public async Task<PlatformStatistics> StatisticsAsync(CancellationToken ct)
    {
        async Task<(long Tokens, decimal Cost)> TotalAsync<T>(IMongoCollection<T> collection)
        {
            var value = await collection.Aggregate().Group<BsonDocument>(new BsonDocument { { "_id", BsonNull.Value }, { "Tokens", new BsonDocument("$sum", "$TokenUsage") }, { "Cost", new BsonDocument("$sum", "$Cost") } }).FirstOrDefaultAsync(ct);
            return value == null ? (0, 0) : (value["Tokens"].ToInt64(), value["Cost"].ToDecimal());
        }
        var model = await TotalAsync(runs); var conversations = await TotalAsync(entries);
        return new(await projects.CountDocumentsAsync(x => x.Status == "Active", cancellationToken: ct),
            await runs.CountDocumentsAsync(x => x.Runnable, cancellationToken: ct),
            await runs.CountDocumentsAsync(x => x.Stage == "Completed", cancellationToken: ct),
            await runs.CountDocumentsAsync(x => x.Stage == "Failed", cancellationToken: ct),
            await entries.CountDocumentsAsync(x => x.Kind == "approval" && x.Status == "Pending", cancellationToken: ct),
            model.Tokens + conversations.Tokens, model.Cost + conversations.Cost,
            await runs.CountDocumentsAsync(FilterDefinition<RunDocument>.Empty, cancellationToken: ct));
    }
    public async Task<IReadOnlyList<RecordEntry>> RecentActivityAsync(CancellationToken ct) => (await entries.Find(x => x.Kind == "event" && (x.Name == "AgentStarted" || x.Name == "AgentHandoffCreated" || x.Name == "ToolInvocationCompleted" || x.Name == "RunCompleted" || x.Name == "ApprovalRequested" || x.Name == "RunFailed")).SortByDescending(x => x.Sequence).Limit(20).ToListAsync(ct)).Select(x => Decode<RecordEntry>(x.Data)!).ToArray();
    public async Task<bool> PingAsync(CancellationToken ct) { await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct); return true; }
    private static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static T? Decode<T>(string? data) => data == null ? default : JsonSerializer.Deserialize<T>(data, Json);
    public sealed class ProjectDocument { public string Status { get; set; } = "Active"; public string Id { get; set; } = ""; public string Data { get; set; } = ""; public long Version { get; set; } public DateTime UpdatedAt { get; set; } }
    public sealed class RunDocument { [BsonRepresentation(BsonType.Decimal128)] public decimal Cost { get; set; } public long TokenUsage { get; set; } public DateTime CreatedAt { get; set; } public List<string> Outbox { get; set; } = []; public string Id { get; set; } = ""; public string ProjectId { get; set; } = ""; public string Key { get; set; } = ""; public string Data { get; set; } = ""; public string Stage { get; set; } = ""; public long Version { get; set; } public bool Active { get; set; } public bool Runnable { get; set; } public string? Owner { get; set; } public DateTime LeaseExpiry { get; set; } }
    public sealed class EntryDocument { public string Name { get; set; } = ""; [BsonRepresentation(BsonType.Decimal128)] public decimal Cost { get; set; } public long TokenUsage { get; set; } public string Id { get; set; } = ""; public string Kind { get; set; } = ""; public string ProjectId { get; set; } = ""; public string? RunId { get; set; } public string? ConversationId { get; set; } public string? ExternalId { get; set; } public string Status { get; set; } = ""; public long Sequence { get; set; } public DateTime CreatedAt { get; set; } public string Data { get; set; } = ""; }
    public sealed class AgentDocument { public string Id { get; set; } = ""; public string Data { get; set; } = ""; }
}
