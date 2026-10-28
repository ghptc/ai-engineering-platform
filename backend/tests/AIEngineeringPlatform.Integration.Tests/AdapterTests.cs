using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using NSubstitute;
namespace AIEngineeringPlatform.Integration.Tests;

public class AdapterTests
{
    [Fact]
    public void GitHubWebhookUsesConstantTimeHmacVerification()
    {
        const string secret = "test-secret"; var body = Encoding.UTF8.GetBytes("{\"action\":\"opened\"}");
        var adapter = new GitHubSourceControl(new HttpClient(), null, null, secret);
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
        Assert.True(adapter.VerifyWebhook(body, signature)); Assert.False(adapter.VerifyWebhook(body, "sha256=00")); Assert.False(adapter.VerifyWebhook(Encoding.UTF8.GetBytes("modified"), signature));
    }
    [Fact]
    public async Task SandboxAdapterSendsBoundedIsolatedRequest()
    {
        var handler = new CaptureHandler(); var adapter = new RemoteExecutionSandbox(new HttpClient(handler), "https://sandbox.example.invalid/", "test-token");
        var result = await adapter.ExecuteAsync(new("operation", "run", new("owner", "repo", 1), "RunTests", JsonSerializer.SerializeToElement(new { profile = "all" })), default);
        Assert.True(result.Success); Assert.Contains("\"networkEnabled\":false", handler.Body); Assert.Contains("\"memoryMb\":512", handler.Body); Assert.Equal("operation", handler.IdempotencyKey);
    }
    [Fact]
    public async Task OpenAIAdapterFailsClosedWithoutCredentials()
    {
        var adapter = new OpenAIProvider(null); var definition = new AgentDefinition("a", AgentRole.Architect, "a", "", "configured-model", "", [], []);
        await Assert.ThrowsAsync<ConfigurationException>(() => adapter.ExecuteAsync(new(new Project(), new ExecutionRun(), "", definition), default));
    }
    [Fact]
    public void OutputSchemaIsStrictAndRequiresOperationalFields()
    {
        using var json = JsonDocument.Parse(OpenAIProvider.OutputSchema); Assert.False(json.RootElement.GetProperty("additionalProperties").GetBoolean()); Assert.Contains(json.RootElement.GetProperty("required").EnumerateArray(), x => x.GetString() == "toolCalls");
    }
    [Fact]
    public async Task OfficialResponsesSdkContractUsesStrictSchemaAndRecordsUsage()
    {
        var handler = new ModelHandler();
        var client = new OpenAI.Responses.ResponsesClient(new System.ClientModel.ApiKeyCredential("test-key"), new OpenAI.Responses.ResponsesClientOptions { Endpoint = new Uri("https://example.invalid/v1/"), Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(new HttpClient(handler)), RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0) });
        var adapter = new OpenAIProvider("test-key", clientOverride: client);
        var definition = new AgentDefinition("architect", AgentRole.Architect, "Architect", "", "test-model", "test instructions", [], [], InputCostPerMillion: 1, OutputCostPerMillion: 2);
        var result = await adapter.ExecuteAsync(new(new Project(), new ExecutionRun(), "test context", definition), default);
        Assert.Equal("resp_contract", result.ResponseId); Assert.Equal(100, result.InputTokens); Assert.Equal(50, result.OutputTokens); Assert.Equal(0.0002m, result.Cost);
        using var body = JsonDocument.Parse(handler.Body); Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString()); Assert.False(body.RootElement.GetProperty("store").GetBoolean()); Assert.Equal("json_schema", body.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
    }
    private sealed class ModelHandler : HttpMessageHandler
    {
        public string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            var text = JsonSerializer.Serialize(new { summary = "Contract fixture", decision = "CONTINUE", acceptanceCriteria = new[] { "Validated contract" }, evidence = Array.Empty<string>(), assumptions = Array.Empty<string>(), toolCalls = Array.Empty<object>(), nextContext = "next", requirements = Array.Empty<object>(), findings = Array.Empty<object>(), checks = Array.Empty<object>(), openQuestions = Array.Empty<string>(), architectureDecisions = Array.Empty<object>() });
            var response = new { id = "resp_contract", @object = "response", created_at = 1700000000, status = "completed", error = (object?)null, incomplete_details = (object?)null, model = "test-model", output = new[] { new { type = "message", id = "msg_contract", status = "completed", role = "assistant", content = new[] { new { type = "output_text", text, annotations = Array.Empty<object>() } } } }, usage = new { input_tokens = 100, output_tokens = 50, total_tokens = 150, input_tokens_details = new { cached_tokens = 0 }, output_tokens_details = new { reasoning_tokens = 0 } } };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") };
        }
    }
    [Fact] public async Task ArtifactToolDoesNotCollideWithItsInvocationCheckpoint() {
        var store = NSubstitute.Substitute.For<IPlatformStore>();
        var tool = new EngineeringTool("SaveArtifact",new DeterministicSandbox(),new DeterministicSourceControl(),store);
        var definition = new AgentDefinition("a",AgentRole.Architect,"a","","test","",[],["SaveArtifact"]);
        var result = await tool.ExecuteAsync(new(new Project(),new ExecutionRun(),"",definition),JsonSerializer.SerializeToElement(new { content = "Verified design" }),"operation-id",default);
        Assert.Equal("artifact-operation-id",result.EvidenceId);
        await store.Received().AppendAsync(NSubstitute.Arg.Is<RecordEntry>(entry => entry.Id == result.EvidenceId && entry.Kind == "artifact"),default);
    }
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string Body = ""; public string IdempotencyKey = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Body = await request.Content!.ReadAsStringAsync(ct); IdempotencyKey = request.Headers.GetValues("Idempotency-Key").Single(); return new(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true,\"output\":\"test evidence\",\"evidenceId\":\"evidence\",\"exitCode\":0,\"workspaceId\":\"run\"}") }; }
    }
}
