using System.Net;
using Xunit;
using CertificationHarness = AIEngineeringPlatform.Certification.Program;
namespace AIEngineeringPlatform.Integration.Tests;
public sealed class CertificationHarnessTests
{
    [Theory]
    [InlineData("--observe --start --password-file private")]
    [InlineData("--strat")]
    [InlineData("--start")]
    [InlineData("--preflight --output")]
    [InlineData("--preflight --preflight")]
    public void AmbiguousOrMistypedActionsAreRejected(string args) => Assert.Throws<ArgumentException>(() => CertificationHarness.ValidateArguments(args.Split(' ')));
    [Fact]
    public void OwnerSettingsAreDataAndDuplicateKeysAreRejected()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "# comment\nSECRET='$(do-not-execute)'\nMODEL=example\n");
            Assert.Equal("$(do-not-execute)", CertificationHarness.ReadSettings(path)["SECRET"]);
            File.AppendAllText(path, "MODEL=duplicate\n");
            Assert.Throws<ArgumentException>(() => CertificationHarness.ReadSettings(path));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task AmbiguousMutationIsNeverRetriedAndDoesNotExposeResponseSecrets()
    {
        using var handler = new Handler(() => new(HttpStatusCode.BadGateway) { Content = new StringContent("secret-server-body") });
        using var http = new HttpClient(handler) { BaseAddress = new("http://test.invalid/") };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CertificationHarness.SendAsync(http, HttpMethod.Post, "projects/test/tasks", new { }, default));
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain("secret-server-body", error.Message);
        Assert.Contains("Reconcile persisted outcome", error.Message);
    }
    [Fact]
    public async Task EvidenceResponseIsBoundedBeforeParsing()
    {
        using var handler = new Handler(() => new(HttpStatusCode.OK) { Content = new StringContent(new string(' ', 8 * 1024 * 1024 + 1)) });
        using var http = new HttpClient(handler) { BaseAddress = new("http://test.invalid/") };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CertificationHarness.SendAsync(http, HttpMethod.Get, "runs/test", null, default));
        Assert.Contains("exceeds bound", error.Message);
    }
    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return Task.FromResult(response()); }
    }
}
