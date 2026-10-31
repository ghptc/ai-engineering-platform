using AIEngineeringPlatform.Application;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AIEngineeringPlatform.Infrastructure;

public sealed class MongoOwnerSessionStore : IOwnerSessionStore
{
    private readonly IMongoCollection<BsonDocument> sessions;
    public MongoOwnerSessionStore(string connection, string database)
    {
        var settings = MongoClientSettings.FromConnectionString(connection);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        settings.SocketTimeout = TimeSpan.FromSeconds(10);
        sessions = new MongoClient(settings).GetDatabase(database).GetCollection<BsonDocument>("owner_sessions_v2");
    }
    public Task InitializeAsync(CancellationToken ct) => sessions.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("ExpiresAt"), new CreateIndexOptions { Name = "owner_session_expiry", ExpireAfter = TimeSpan.Zero }), cancellationToken: ct);
    public Task CreateAsync(OwnerSession session, CancellationToken ct) => sessions.InsertOneAsync(new BsonDocument { ["_id"] = session.Id, ["Ticket"] = new BsonBinaryData(session.ProtectedTicket), ["AuthenticatedAt"] = session.AuthenticatedAt.UtcDateTime, ["ExpiresAt"] = session.ExpiresAt.UtcDateTime }, cancellationToken: ct);
    public async Task<OwnerSession?> FindAsync(string id, DateTimeOffset now, CancellationToken ct)
    {
        // TTL deletion is asynchronous; authorization always checks the absolute expiry itself.
        var result = await sessions.Find(Builders<BsonDocument>.Filter.Eq("_id", id) & Builders<BsonDocument>.Filter.Gt("ExpiresAt", now.UtcDateTime)).FirstOrDefaultAsync(ct);
        return result == null ? null : new(id, result["Ticket"].AsByteArray, new DateTimeOffset(result["AuthenticatedAt"].ToUniversalTime()), new DateTimeOffset(result["ExpiresAt"].ToUniversalTime()));
    }
    public Task RevokeAsync(string id, CancellationToken ct) => sessions.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", id), ct);
}
