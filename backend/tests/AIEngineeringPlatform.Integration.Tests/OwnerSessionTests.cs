using AIEngineeringPlatform.Api;
using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Xunit;

namespace AIEngineeringPlatform.Integration.Tests;

public sealed class OwnerSessionTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [Fact]
    public async Task PersistentStoreSurvivesRecreationAndEnforcesAbsoluteExpiryAndRevocation()
    {
        var connection = Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028";
        var database = "aep_sessions_" + Guid.NewGuid().ToString("N");
        var store = new MongoOwnerSessionStore(connection, database);
        await store.InitializeAsync(default);
        var protection = new EphemeralDataProtectionProvider();
        var clock = new Clock();
        var options = Options.Create(new OwnerOptions { CredentialHash = "test-only-owner-credential-version" });
        var first = new OwnerTicketStore(store, protection, clock, options, new Microsoft.AspNetCore.Http.HttpContextAccessor());
        AuthenticationTicket Ticket() => new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "owner")], "Cookies")), new AuthenticationProperties { IssuedUtc = clock.Now }, "Cookies");
        var key = await first.StoreAsync(Ticket());
        var replacement = new OwnerTicketStore(new MongoOwnerSessionStore(connection, database), protection, clock, options, new Microsoft.AspNetCore.Http.HttpContextAccessor());
        Assert.NotNull(await replacement.RetrieveAsync(key));
        clock.Now += TimeSpan.FromHours(7);
        Assert.NotNull(await replacement.RetrieveAsync(key));
        clock.Now += TimeSpan.FromHours(1);
        Assert.Null(await replacement.RetrieveAsync(key));
        var current = await replacement.StoreAsync(Ticket());
        Assert.NotEqual(key, current);
        await replacement.RemoveAsync(current);
        Assert.Null(await first.RetrieveAsync(current));
        Assert.Null(await first.RetrieveAsync("tampered"));
    }
    [Fact]
    public async Task CredentialRotationAndTamperedStoredTicketsAreRejected()
    {
        var connection = Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028";
        var store = new MongoOwnerSessionStore(connection, "aep_sessions_" + Guid.NewGuid().ToString("N"));
        var clock = new Clock(); var protection = new EphemeralDataProtectionProvider();
        var original = new OwnerTicketStore(store, protection, clock, Options.Create(new OwnerOptions { CredentialHash = "original" }), new Microsoft.AspNetCore.Http.HttpContextAccessor());
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "owner")], "Cookies")), new AuthenticationProperties { IssuedUtc = clock.Now }, "Cookies");
        var key = await original.StoreAsync(ticket);
        var rotated = new OwnerTicketStore(store, protection, clock, Options.Create(new OwnerOptions { CredentialHash = "rotated" }), new Microsoft.AspNetCore.Http.HttpContextAccessor());
        Assert.Null(await rotated.RetrieveAsync(key));
        var differentProtection = new OwnerTicketStore(store, new EphemeralDataProtectionProvider(), clock, Options.Create(new OwnerOptions { CredentialHash = "original" }), new Microsoft.AspNetCore.Http.HttpContextAccessor());
        Assert.Null(await differentProtection.RetrieveAsync(key));
    }
    [Fact]
    public async Task BrowserCookieSurvivesApiRestartAndLogoutRevokesCapturedCookie()
    {
        var keys = Path.Combine(Path.GetTempPath(), "aep-owner-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keys);
        try
        {
            await using var root = new PlatformFactory();
            string cookie;
            await using (var first = root.WithWebHostBuilder(builder => builder.UseSetting("AUTH_KEY_RING_PATH", keys)))
            {
                using var client = first.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), HandleCookies = false });
                client.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
                var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { password = PlatformFactory.Password });
                login.EnsureSuccessStatusCode();
                var header = login.Headers.GetValues("Set-Cookie").LastOrDefault(x => x.StartsWith("aep.owner=") && !x.StartsWith("aep.owner=;"));
                Assert.True(header != null, "Persistent owner cookie was not issued.");
                Assert.True(header.Contains("expires=") && header.Contains("httponly") && header.Contains("samesite=strict"), "Owner cookie security attributes are incomplete.");
                cookie = header.Split(';')[0];
            }
            await using var restarted = root.WithWebHostBuilder(builder => builder.UseSetting("AUTH_KEY_RING_PATH", keys));
            using var restored = restarted.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), HandleCookies = false });
            restored.DefaultRequestHeaders.Add("Cookie", cookie);
            restored.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
            Assert.Equal(HttpStatusCode.OK, (await restored.GetAsync("/api/v1/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await restored.PostAsJsonAsync("/api/v1/auth/logout", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await restored.GetAsync("/api/v1/auth/me")).StatusCode);
        }
        finally { Directory.Delete(keys, true); }
    }
    [Fact]
    public async Task CrossOriginMutationsAreRejectedEvenWithTheCustomHeader()
    {
        await using var factory = new PlatformFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/auth/login", new { password = PlatformFactory.Password })).StatusCode);
    }
    [Fact]
    public async Task RevokedAndExpiredSessionsLoseLiveConnectionsBeforePublication()
    {
        var sessions = NSubstitute.Substitute.For<IOwnerSessionStore>();
        var connections = new OwnerConnections(sessions, TimeProvider.System);
        var session = new string('A', 64);
        var aborted = 0;
        connections.Add("tab-one", session, () => aborted++);
        connections.Add("tab-two", session, () => aborted++);
        await connections.PruneAsync(default); // An absent/expired authority record cannot receive broadcasts.
        Assert.Equal(2, aborted);
        connections.Add("tab-three", session, () => aborted++);
        connections.Revoke(session);
        Assert.Equal(3, aborted);
    }

    [Fact]
    public async Task ReauthenticationRotatesIdentityAndRevokesThePreviousCookie()
    {
        await using var factory = new PlatformFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
        async Task<string> Login()
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { password = PlatformFactory.Password });
            response.EnsureSuccessStatusCode();
            var header = response.Headers.GetValues("Set-Cookie").LastOrDefault(value => value.StartsWith("aep.owner=") && !value.StartsWith("aep.owner=;"));
            Assert.True(header != null, "Owner cookie missing.");
            return header.Split(';')[0];
        }
        var original = await Login(); client.DefaultRequestHeaders.Add("Cookie", original);
        var replacement = await Login();
        Assert.True(original != replacement, "Authentication must issue a fresh session identity.");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Add("Cookie", replacement);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

}
