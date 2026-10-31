using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Infrastructure;
using Xunit;

namespace AIEngineeringPlatform.Integration.Tests;

public sealed class ExecutorHttpTests
{
    [Fact]
    public async Task ReceiptRetentionConflictHasKnownOutcomeAndIsNeverRetried()
    {
        var handler = new Handler(_ => new(HttpStatusCode.Conflict) { Content = new StringContent("{\"code\":\"operation_retention_limit\",\"detail\":\"secret path\"}") });
        var error = await Assert.ThrowsAsync<ExecutorFailureException>(() => Sandbox(handler).ExecuteAsync(Request("ListFiles"), default));
        Assert.Equal(ExecutorFailureReason.OperationRetentionLimit, error.Reason);
        Assert.Equal(ExecutorFailureCategory.WorkspaceConflict, error.Category);
        Assert.False(error.OutcomeUnknown);
        Assert.Single(handler.Keys);
        Assert.DoesNotContain("secret", error.Message);
    }
    [Theory]
    [InlineData(400, ExecutorFailureCategory.InvalidArguments)]
    [InlineData(401, ExecutorFailureCategory.Authorization)]
    [InlineData(403, ExecutorFailureCategory.Authorization)]
    [InlineData(404, ExecutorFailureCategory.RepositoryAccess)]
    [InlineData(409, ExecutorFailureCategory.WorkspaceConflict)]
    [InlineData(422, ExecutorFailureCategory.ProfileRestriction)]
    [InlineData(429, ExecutorFailureCategory.RateLimited)]
    [InlineData(500, ExecutorFailureCategory.Infrastructure)]
    [InlineData(502, ExecutorFailureCategory.Infrastructure)]
    [InlineData(503, ExecutorFailureCategory.Infrastructure)]
    [InlineData(504, ExecutorFailureCategory.Timeout)]
    public async Task MutationIsNeverRetriedAndErrorIsSafe(int status, ExecutorFailureCategory category)
    {
        var handler = new Handler(_ => new((HttpStatusCode)status) { Content = new StringContent("{\"detail\":\"Bearer secret /private/path <script>unsafe</script>\"}") });
        var error = await Assert.ThrowsAsync<ExecutorFailureException>(() => Sandbox(handler).ExecuteAsync(Request("WriteFile"), default));
        Assert.Equal(category, error.Category); Assert.Equal(status, error.StatusCode);
        Assert.Equal("operation", error.OperationId); Assert.Single(handler.Keys);
        Assert.DoesNotContain("secret", error.Message); Assert.DoesNotContain("private", error.Message);
    }

    [Theory]
    [InlineData(429)] [InlineData(502)] [InlineData(504)]
    public async Task InspectionsRetryWithSameIdentityWithinBound(int status)
    {
        var handler = new Handler(n => n < 3 ? new((HttpStatusCode)status) : new(HttpStatusCode.OK) { Content = JsonContent.Create(new SandboxResponse(true, "ok", "receipt", 0, "run", "revision")) });
        var result = await Sandbox(handler).ExecuteAsync(Request("ReadFile"), default);
        Assert.True(result.Success); Assert.Equal(3, handler.Keys.Count);
        Assert.All(handler.Keys, key => Assert.Equal("operation", key));
    }

    [Theory]
    [InlineData(0, 429)]
    [InlineData(2, 503)]
    [InlineData(2, 500)]
    public async Task DisabledRetriesAndUnqualifiedServerFailuresStopImmediately(int retries, int status)
    {
        var handler = new Handler(_ => new((HttpStatusCode)status));
        await Assert.ThrowsAsync<ExecutorFailureException>(() => Sandbox(handler).ExecuteAsync(Request("ReadFile") with { MaxTransientRetries = retries }, default));
        Assert.Single(handler.Keys);
    }

    [Theory]
    [InlineData(1, 3)] [InlineData(10, 1)]
    public async Task RetryAfterIsRespectedWithoutExceedingAttemptBound(int seconds, int attempts)
    {
        var handler = new Handler(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            return response;
        });
        await Assert.ThrowsAsync<ExecutorFailureException>(() => Sandbox(handler).ExecuteAsync(Request("ReadFile"), default));
        Assert.Equal(attempts, handler.Keys.Count);
    }

    [Fact]
    public async Task OversizedAndMalformedBodiesAreDiscarded()
    {
        foreach (var body in new[] { new string('x', 1000000), "<html>secret</html>", "[]" })
        {
            var error = await Assert.ThrowsAsync<ExecutorFailureException>(() => Sandbox(new Handler(_ => new(HttpStatusCode.Conflict) { Content = new StringContent(body) })).ExecuteAsync(Request("WriteFile"), default));
            Assert.Equal(ExecutorFailureCategory.WorkspaceConflict, error.Category);
            Assert.True(error.Message.Length < 600);
        }
    }

    [Fact]
    public async Task ContractCodeDistinguishesRepositoryFromAuthorization()
    {
        var error = await Assert.ThrowsAsync<ExecutorFailureException>(() => Sandbox(new Handler(_ => new(HttpStatusCode.Forbidden) { Content = new StringContent("{\"code\":\"repository_access\"}") })).ExecuteAsync(Request("WriteFile"), default));
        Assert.Equal(ExecutorFailureCategory.RepositoryAccess, error.Category);
    }

    [Fact]
    public async Task TransportFailureRequiresReconciliationAndCancellationPropagates()
    {
        var error = await Assert.ThrowsAsync<ExecutorFailureException>(() => Sandbox(new Handler(_ => throw new HttpRequestException("secret"))).ExecuteAsync(Request("WriteFile"), default));
        Assert.True(error.OutcomeUnknown); Assert.DoesNotContain("secret", error.Message);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sandbox(new Handler(_ => throw new OperationCanceledException())).ExecuteAsync(Request("WriteFile"), cancellation.Token));
    }

    [Theory]
    [InlineData(503, "image_unavailable", ExecutorFailureReason.ImageUnavailable)]
    [InlineData(422, "profile_target_missing", ExecutorFailureReason.ProfileTargetMissing)]
    public async Task VerifiedPreflightRejectionIsKnownNotStarted(int status, string code, ExecutorFailureReason reason)
    {
        var handler = new Handler(_ => new((HttpStatusCode)status) { Content = JsonContent.Create(new { code, detail = "secret daemon text" }) });
        var error = await Assert.ThrowsAsync<ExecutorFailureException>(() => Sandbox(handler).ExecuteAsync(Request("RunCommand"), default));
        Assert.Equal(reason, error.Reason); Assert.False(error.OutcomeUnknown);
        Assert.DoesNotContain("secret", error.Message); Assert.Contains("no workload was started", error.Message);
    }

    private static SandboxRequest Request(string operation) => new("operation", "run", new("owner", "repo", 1), operation, JsonSerializer.SerializeToElement(new { path = "file" }), MaxTransientRetries: 2);
    private static RemoteExecutionSandbox Sandbox(Handler handler) => new(new HttpClient(handler), "https://executor.invalid/", "control-token");
    private sealed class Handler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Keys { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            return Task.FromResult(response(Keys.Count));
        }
    }
}
