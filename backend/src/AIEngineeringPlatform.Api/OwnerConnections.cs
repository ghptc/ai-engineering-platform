using System.Collections.Concurrent;
using AIEngineeringPlatform.Application;
namespace AIEngineeringPlatform.Api;

// Only transport connections live in memory. Session authority remains in Mongo across replicas.
public sealed class OwnerConnections(IOwnerSessionStore sessions, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, (string Session, Action Abort)> connections = new();
    public void Add(string connection, string session, Action abort)
    {
        if (session.Length != 64 || !session.All(char.IsAsciiHexDigit) || connections.Count >= 64) throw new UnauthorizedAccessException("Owner session connection is unavailable.");
        connections[connection] = (session, abort);
    }
    public void Remove(string connection) => connections.TryRemove(connection, out _);
    public void Revoke(string session)
    {
        foreach (var entry in connections.Where(x => x.Value.Session == session))
            if (connections.TryRemove(entry.Key, out var removed)) removed.Abort();
    }
    public async Task PruneAsync(CancellationToken ct)
    {
        foreach (var session in connections.Values.Select(x => x.Session).Distinct())
            if (await sessions.FindAsync(session, clock.GetUtcNow(), ct) == null) Revoke(session);
    }
}
