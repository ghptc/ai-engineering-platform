using AIEngineeringPlatform.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace AIEngineeringPlatform.Api;

public sealed class OwnerTicketStore(IOwnerSessionStore sessions, IDataProtectionProvider protection, TimeProvider clock, IOptions<OwnerOptions> owner, IHttpContextAccessor accessor)
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(8);
    private readonly IDataProtector protector = protection.CreateProtector("AEP.OwnerSessions.v2");
    private static string Id(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    private string CredentialVersion => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner.Value.CredentialHash)));
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var now = clock.GetUtcNow();
        var authenticated = DateTimeOffset.FromUnixTimeMilliseconds((ticket.Properties.IssuedUtc ?? now).ToUnixTimeMilliseconds());
        var expires = authenticated + MaximumLifetime;
        if (authenticated > now || expires <= now) throw new InvalidOperationException("Invalid session authentication time.");
        ticket.Properties.IssuedUtc = authenticated;
        ticket.Properties.ExpiresUtc = expires;
        ticket.Properties.AllowRefresh = false;
        ticket.Properties.Items["ownerCredentialVersion"] = CredentialVersion;
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        foreach (var identity in ticket.Principal.Identities)
            foreach (var claim in identity.FindAll("aep.session").ToArray()) identity.RemoveClaim(claim);
        ticket.Principal.Identities.First().AddClaim(new System.Security.Claims.Claim("aep.session", Id(key)));
        await sessions.CreateAsync(new(Id(key), protector.Protect(TicketSerializer.Default.Serialize(ticket)), authenticated, expires), accessor.HttpContext?.RequestAborted ?? CancellationToken.None);
        return key;
    }
    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (key.Length != 64 || !key.All(char.IsAsciiHexDigit)) return null;
        return await RetrieveByIdAsync(Id(key));
    }
    public async Task<AuthenticationTicket?> RetrieveByIdAsync(string id)
    {
        if (id.Length != 64 || !id.All(char.IsAsciiHexDigit)) return null;
        var now = clock.GetUtcNow();
        var session = await sessions.FindAsync(id, now, accessor.HttpContext?.RequestAborted ?? CancellationToken.None);
        if (session == null || session.ExpiresAt <= now || session.AuthenticatedAt > now || session.ExpiresAt - session.AuthenticatedAt > MaximumLifetime) return null;
        try
        {
            var ticket = TicketSerializer.Default.Deserialize(protector.Unprotect(session.ProtectedTicket));
            return ticket != null && ticket.Properties.Items.TryGetValue("ownerCredentialVersion", out var version) && version == CredentialVersion && ticket.Properties.ExpiresUtc == session.ExpiresAt && ticket.Properties.IssuedUtc == session.AuthenticatedAt ? ticket : null;
        }
        catch (CryptographicException) { return null; }
    }
    public Task RevokeByIdAsync(string id) => id.Length == 64 && id.All(char.IsAsciiHexDigit) ? sessions.RevokeAsync(id, accessor.HttpContext?.RequestAborted ?? CancellationToken.None) : Task.CompletedTask;
    public Task RemoveAsync(string key) => sessions.RevokeAsync(Id(key), accessor.HttpContext?.RequestAborted ?? CancellationToken.None);
}
public sealed class OwnerCookieConfiguration(OwnerTicketStore store) : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name != CookieAuthenticationDefaults.AuthenticationScheme) return;
        // CookieHandler reuses an existing ITicketStore key on reauthentication. Explicit
        // session events instead issue a new identity and never resurrect a revoked key.
        options.SessionStore = null;
        options.Events.OnSigningIn = async context =>
        {
            await store.StoreAsync(new AuthenticationTicket(context.Principal!, context.Properties, context.Scheme.Name));
        };
        options.Events.OnValidatePrincipal = async context =>
        {
            var id = context.Principal?.FindFirst("aep.session")?.Value;
            var ticket = id == null ? null : await store.RetrieveByIdAsync(id);
            if (ticket == null || ticket.Properties.IssuedUtc != context.Properties.IssuedUtc || ticket.Properties.ExpiresUtc != context.Properties.ExpiresUtc) context.RejectPrincipal();
            else context.ReplacePrincipal(ticket.Principal);
        };
        options.Events.OnSigningOut = async context =>
        {
            if (context.HttpContext.User.FindFirst("aep.session")?.Value is { } id) await store.RevokeByIdAsync(id);
        };
    }
}
public sealed class OwnerSessionInitialization(IOwnerSessionStore sessions) : IHostedService
{
    public Task StartAsync(CancellationToken ct) => sessions.InitializeAsync(ct);
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
