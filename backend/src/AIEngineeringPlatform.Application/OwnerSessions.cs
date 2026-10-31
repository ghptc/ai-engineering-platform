namespace AIEngineeringPlatform.Application;

// Session identifiers are hashed before persistence; protected tickets never enter agent context.
public sealed record OwnerSession(string Id, byte[] ProtectedTicket, DateTimeOffset AuthenticatedAt, DateTimeOffset ExpiresAt);
public interface IOwnerSessionStore
{
    Task InitializeAsync(CancellationToken ct);
    Task CreateAsync(OwnerSession session, CancellationToken ct);
    Task<OwnerSession?> FindAsync(string id, DateTimeOffset now, CancellationToken ct);
    Task RevokeAsync(string id, CancellationToken ct);
}
