using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Application.Dependencies;
using AIEngineeringPlatform.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace AIEngineeringPlatform.Integration.Tests;

public sealed class DependencyControlTransportTests
{
    [Fact]
    public async Task ExplicitReconciliationUsesPostAndRetainsSafeFailureClassification()
    {
        var failure = new DependencyFailure("reconciliation", "dependency_acquisition_interrupted", true, true, "RepairConfiguration");
        using var handler = new ReplyHandler(JsonContent.Create(new { dependencyFailure = failure }));
        using var http = new HttpClient(handler);
        var executor = new RemoteExecutionSandbox(http, "https://executor.example/", "test-only-token");
        var error = await Assert.ThrowsAsync<DependencyAcquisitionException>(() => executor.ReconcileAsync(new string('a', 64), default));
        Assert.Equal(failure, error.Failure);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.EndsWith("/reconcile", handler.Path);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"dependencyFailure\":{\"phase\":null,\"code\":null}}")]
    [InlineData("{\"dependencyFailure\":{\"phase\":\"/private/path\",\"code\":\"secret\",\"outcomeKnown\":true,\"recoveryAction\":\"RepairConfiguration\"}}")]
    public async Task UnclassifiedResponsesRemainUncertainAndNeverForwardRawDetails(string body)
    {
        using var handler = new ReplyHandler(new StringContent(body)); using var http = new HttpClient(handler);
        var executor = new RemoteExecutionSandbox(http, "https://executor.example/", "test-only-token");
        var error = await Assert.ThrowsAsync<ConfigurationException>(() => executor.ReconcileAsync(new string('a', 64), default));
        Assert.Contains("reconcile", error.Message);
        Assert.DoesNotContain("/private/path", error.Message);
        Assert.DoesNotContain("secret", error.Message);
    }

    private sealed class ReplyHandler(HttpContent content) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Method = request.Method; Path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = content });
        }
    }
}
