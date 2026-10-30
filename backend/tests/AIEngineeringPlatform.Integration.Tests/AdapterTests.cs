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
    public async Task MemoryRetrievalIsBoundedTraceableAndProjectScoped()
    {
        var store = Substitute.For<IPlatformStore>();
        store.GetRecordAsync("tool", "original", default).Returns(new RecordEntry { Id = "original", Kind = "tool", ProjectId = "p", Content = new string('a', 5000) + "TARGET" });
        var tool = new EngineeringTool("ReadProjectMemory", new DeterministicSandbox(), new DeterministicSourceControl(), store);
        var definition = new AgentDefinition("a", AgentRole.Architect, "a", "", "test", "", [], ["ReadProjectMemory"]);
        var context = new AgentContext(new Project { Id = "p" }, new ExecutionRun(), "", definition);
        var args = JsonSerializer.SerializeToElement(new { kind = "tool", id = "original", offset = 5000, limit = 6 });
        var result = await tool.ExecuteAsync(context, args, "read", default);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("TARGET", json.RootElement.GetProperty("Content").GetString());
        Assert.Equal(5006, json.RootElement.GetProperty("TotalCharacters").GetInt32());
        Assert.Contains("Sha256", result.Output);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => tool.ExecuteAsync(context with { Project = new Project { Id = "other" } }, args, "read", default));
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ExecuteAsync(context, JsonSerializer.SerializeToElement(new { id = "original", limit = 4001 }), "read", default));
    }
    [Fact]
    public async Task ProviderRejectsOversizedProtectedInputBeforeSendingRequest()
    {
        var handler = new ModelHandler();
        var client = new OpenAI.Responses.ResponsesClient(new System.ClientModel.ApiKeyCredential("test-key"), new OpenAI.Responses.ResponsesClientOptions { Endpoint = new Uri("https://example.invalid/v1/"), Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(new HttpClient(handler)) });
        var definition = new AgentDefinition("a", AgentRole.Architect, "a", "", "test-model", "instructions", [], [], InputCostPerMillion: 1, OutputCostPerMillion: 2, ModelContextTokens: 20000, MaxInputTokens: 16000);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenAIProvider("test-key", clientOverride: client).ExecuteAsync(new(new Project(), new ExecutionRun(), new string('x', 15000), definition), default));
        Assert.Empty(handler.Body);
    }
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
        Assert.Equal("Verify offline cache", result.Output.Planning!.DependencyStrategy);
        using var body = JsonDocument.Parse(handler.Body); Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString()); Assert.False(body.RootElement.GetProperty("store").GetBoolean()); Assert.Equal("json_schema", body.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        Assert.Equal("json_schema_strict", result.Diagnostics!.OutputFormat);
        Assert.Equal("Validated", result.Diagnostics.OutputStage);
    }
    private static (OpenAIProvider Provider, AgentContext Context, IPlatformStore Store, List<RecordEntry> Records) Fixture(ModelHandler handler, AgentRole role = AgentRole.BackendEngineer, LanguageModelOptions? options = null)
    {
        var records = new List<RecordEntry>();
        var store = Substitute.For<IPlatformStore>();
        store.RecordsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(c => (IReadOnlyList<RecordEntry>)records.Where(x => x.Kind == c.ArgAt<string>(0)).Skip(c.ArgAt<int>(5)).Take(c.ArgAt<int>(6)).ToArray());
        store.AppendAsync(Arg.Any<RecordEntry>(), Arg.Any<CancellationToken>()).Returns(c => { records.Add(c.ArgAt<RecordEntry>(0)); return Task.CompletedTask; });
        var client = new OpenAI.Responses.ResponsesClient(new System.ClientModel.ApiKeyCredential("test-key"), new OpenAI.Responses.ResponsesClientOptions { Endpoint = new Uri("https://example.invalid/v1/"), Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(new HttpClient(handler)), RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0) });
        var definition = new AgentDefinition("a", role, "Agent", "", "test-model", "test instructions", [], ["WriteFile", "CreateCommit", "CreatePullRequest"], InputCostPerMillion: 1, OutputCostPerMillion: 2);
        return (new("test-key", options, client, store), new(new Project(), new ExecutionRun(), "context", definition), store, records);
    }
    private static void Output(System.Text.Json.Nodes.JsonObject payload, Func<System.Text.Json.Nodes.JsonObject, string> transform)
    {
        var content = payload["output"]![0]!["content"]![0]!;
        var original = System.Text.Json.Nodes.JsonNode.Parse(content["text"]!.GetValue<string>())!.AsObject();
        content["text"] = transform(original);
    }
    [Theory]
    [InlineData("malformed", ProviderErrorCategory.MalformedJson)]
    [InlineData("truncated", ProviderErrorCategory.MalformedJson)]
    [InlineData("empty", ProviderErrorCategory.EmptyOutput)]
    [InlineData("arguments", ProviderErrorCategory.InvalidToolArguments)]
    [InlineData("arrayArguments", ProviderErrorCategory.InvalidToolArguments)]
    [InlineData("schema", ProviderErrorCategory.InvalidSchema)]
    [InlineData("incomplete", ProviderErrorCategory.IncompleteResponse)]
    [InlineData("status", ProviderErrorCategory.UnexpectedContent)]
    [InlineData("failed", ProviderErrorCategory.ResponseFailed)]
    [InlineData("usage", ProviderErrorCategory.MissingUsage)]
    [InlineData("refusal", ProviderErrorCategory.UnexpectedContent)]
    [InlineData("noMessages", ProviderErrorCategory.EmptyOutput)]
    public async Task ProviderValidatesBeforeAnyToolCanExecute(string scenario, ProviderErrorCategory category)
    {
        var handler = new ModelHandler { Transform = payload =>
        {
            if (scenario == "refusal") { payload["output"]![0]!["content"]![0] = System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"refusal\",\"refusal\":\"sensitive refusal\"}"); }
            else if (scenario == "noMessages") payload["output"] = new System.Text.Json.Nodes.JsonArray();
            else if (scenario == "incomplete") { payload["status"] = "incomplete"; payload["incomplete_details"] = new System.Text.Json.Nodes.JsonObject { ["reason"] = "max_output_tokens" }; }
            else if (scenario == "status") payload["status"] = "in_progress";
            else if (scenario == "failed") payload["status"] = "failed";
            else if (scenario == "usage") payload["usage"] = null;
            else Output(payload, output =>
            {
                if (scenario == "malformed") return "sensitive-invalid-output";
                if (scenario == "truncated") return "{\"summary\":\"unfinished";
                if (scenario == "empty") return "";
                if (scenario == "schema") output.Remove("summary");
                else output["toolCalls"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"name\":\"WriteFile\",\"arguments\":\"{}\"},{\"name\":\"CreateCommit\",\"arguments\":\"" + (scenario == "arrayArguments" ? "[]" : "sensitive-invalid-arguments") + "\"}]");
                return output.ToJsonString();
            });
        } };
        var f = Fixture(handler, options: new() { MaxMalformedResponseRetries = 0 });
        var error = await Assert.ThrowsAsync<ProviderException>(() => f.Provider.ExecuteAsync(f.Context, default));
        Assert.Equal(category, error.Diagnostics.ErrorCategory);
        Assert.Equal("req_contract", error.Diagnostics.RequestId);
        Assert.Equal(scenario == "usage" ? (long?)null : 100, error.Diagnostics.InputTokens);
        Assert.DoesNotContain("sensitive", JsonSerializer.Serialize(error.Diagnostics));
        Assert.DoesNotContain("sensitive", error.ToString());
        Assert.Equal(1, handler.Requests);
        Assert.DoesNotContain(f.Records, x => x.Kind is "tool" or "task");
        Assert.True((await ProviderAccounting.ChargesAsync(f.Store, f.Context.Run.Id, default)).Single().Cost >= .0002m);
    }
    [Theory]
    [InlineData(AgentRole.BusinessAnalyst)] [InlineData(AgentRole.Architect)]
    [InlineData(AgentRole.BackendEngineer)] [InlineData(AgentRole.FrontendEngineer)]
    [InlineData(AgentRole.TestEngineer)] [InlineData(AgentRole.QAEngineer)] [InlineData(AgentRole.CodeReviewer)]
    public async Task EveryRoleUsesTheSameValidatedProvider(AgentRole role)
    {
        var handler = new ModelHandler(); var f = Fixture(handler, role);
        var result = await f.Provider.ExecuteAsync(f.Context, default);
        Assert.Equal("completed", result.Diagnostics!.ResponseStatus);
        Assert.Equal("req_contract", result.Diagnostics.RequestId);
        Assert.Equal(result.Cost, (await ProviderAccounting.ChargesAsync(f.Store, f.Context.Run.Id, default)).Single().Cost);
    }
    [Fact]
    public async Task CallerCancellationPropagatesWithoutRetry()
    {
        using var caller = new CancellationTokenSource();
        var handler = new ModelHandler { Before = (_, ct) => { caller.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; } };
        var f = Fixture(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Provider.ExecuteAsync(f.Context, caller.Token));
        Assert.Equal(1, handler.Requests);
        Assert.Equal(ProviderErrorCategory.CallerCancellation, (await ProviderAccounting.ChargesAsync(f.Store, f.Context.Run.Id, default)).Single().Diagnostics.ErrorCategory);
    }
    [Fact]
    public async Task SdkTimeoutIsDistinctFromCallerCancellation()
    {
        var handler = new ModelHandler { Before = (_, _) => throw new TaskCanceledException() }; var f = Fixture(handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => f.Provider.ExecuteAsync(f.Context, default));
        Assert.Equal(ProviderErrorCategory.SdkTimeout, error.Diagnostics.ErrorCategory);
        Assert.Equal(1, handler.Requests);
    }
    [Fact]
    public async Task ProviderDeadlineIsTypedAndDoesNotReplayStage()
    {
        var handler = new ModelHandler { Before = (_, ct) => Task.Delay(Timeout.Infinite, ct) };
        var f = Fixture(handler, options: new() { TimeoutSeconds = 5 });
        var error = await Assert.ThrowsAsync<ProviderException>(() => f.Provider.ExecuteAsync(f.Context, default));
        Assert.Equal(ProviderErrorCategory.RequestTimeout, error.Diagnostics.ErrorCategory);
        Assert.Equal(1, handler.Requests);
    }
    [Fact]
    public async Task RunDeadlineIsDistinctAndBounded()
    {
        var handler = new ModelHandler { Before = (_, ct) => Task.Delay(Timeout.Infinite, ct) }; var f = Fixture(handler);
        var context = f.Context with { Run = f.Context.Run with { StartedAt = DateTimeOffset.UtcNow.AddMinutes(-60).AddSeconds(1) } };
        var error = await Assert.ThrowsAsync<ProviderException>(() => f.Provider.ExecuteAsync(context, default));
        Assert.Equal(ProviderErrorCategory.RunDeadline, error.Diagnostics.ErrorCategory);
    }
    [Fact]
    public async Task TransientRetryOnlyRepeatsModelRequestAndPreservesCheckpoints()
    {
        var handler = new ModelHandler { Before = (n, _) => n == 1 ? throw new HttpRequestException("sensitive transport details") : Task.CompletedTask };
        var f = Fixture(handler);
        var checkpoints = new[] { "WriteFile", "CreateCommit", "CreatePullRequest" }.Select(name => new RecordEntry { Id = name + "-operation", Kind = "tool", Name = name, Content = "workspace-revision-and-artifact", Status = "Succeeded" }).ToArray();
        f.Records.AddRange(checkpoints);
        await f.Provider.ExecuteAsync(f.Context, default);
        Assert.Equal(2, handler.Requests);
        Assert.Equal(checkpoints, f.Records.Where(x => x.Kind == "tool"));
        var charges = await ProviderAccounting.ChargesAsync(f.Store, f.Context.Run.Id, default);
        Assert.Equal(2, charges.Length);
        Assert.Contains(charges, x => x.Diagnostics.ErrorCategory == ProviderErrorCategory.TransportFailure && x.Cost > 0);
        Assert.DoesNotContain("sensitive", JsonSerializer.Serialize(charges));
    }
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, 2)]
    [InlineData(HttpStatusCode.Unauthorized, 1)]
    public async Task HttpFailuresAreSanitizedAndRetriesAreBounded(HttpStatusCode status, int count)
    {
        var handler = new ModelHandler { Status = _ => status }; var f = Fixture(handler, options: new() { MaxRetries = 1 });
        var error = await Assert.ThrowsAsync<ProviderException>(() => f.Provider.ExecuteAsync(f.Context, default));
        Assert.Equal(ProviderErrorCategory.HttpFailure, error.Diagnostics.ErrorCategory);
        Assert.Equal((int)status, error.Diagnostics.HttpStatus);
        Assert.Equal("req_contract", error.Diagnostics.RequestId);
        Assert.Equal(count, handler.Requests);
    }
    [Fact]
    public async Task BudgetReservationSurvivesFailureAndPreventsAnotherRequest()
    {
        var handler = new ModelHandler { Before = (_, _) => throw new HttpRequestException() }; var f = Fixture(handler, options: new() { MaxRetries = 0 });
        await Assert.ThrowsAsync<ProviderException>(() => f.Provider.ExecuteAsync(f.Context, default));
        var charge = (await ProviderAccounting.ChargesAsync(f.Store, f.Context.Run.Id, default)).Single();
        var context = f.Context with { Run = f.Context.Run with { Policy = f.Context.Run.Policy with { MaxBudgetUsd = charge.Cost } } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Provider.ExecuteAsync(context, default));
        Assert.Equal(1, handler.Requests);
    }
    [Theory]
    [InlineData("max_output_tokens", "OutputLimit")]
    [InlineData("content_filter", "ContentFilter")]
    [InlineData("steered", "Steered")]
    [InlineData("unknown", "Unknown")]
    public async Task IncompleteReasonIsClassifiedWithoutGuessing(string reason, string classification)
    {
        var handler = new ModelHandler { Transform = payload =>
        {
            payload["status"] = "incomplete";
            payload["incomplete_details"] = new System.Text.Json.Nodes.JsonObject { ["reason"] = reason };
        } };
        var f = Fixture(handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => f.Provider.ExecuteAsync(f.Context, default));
        Assert.Equal(classification, error.Diagnostics.IncompleteClassification);
        Assert.NotNull(error.Diagnostics.RecoveryGuidance);
        Assert.Equal(4000, error.Diagnostics.MaxOutputTokens);
        Assert.Equal(1, handler.Requests);
    }
    private static void Exhaust(System.Text.Json.Nodes.JsonObject payload)
    {
        payload["status"] = "incomplete";
        payload["incomplete_details"] = new System.Text.Json.Nodes.JsonObject { ["reason"] = "max_output_tokens" };
        payload["usage"]!["output_tokens"] = 4000;
        // Even a valid-looking side effect in an incomplete response must never escape.
        Output(payload, output => { output["toolCalls"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"name\":\"CreateCommit\",\"arguments\":\"{}\"}]"); return output.ToJsonString(); });
    }
    [Fact]
    public async Task ExhaustedOutputRecoversWithAccountedLargerRequestAndPreservesEvidence()
    {
        var handler = new ModelHandler();
        handler.Transform = payload => { if (handler.Requests == 1) Exhaust(payload); };
        var f = Fixture(handler, AgentRole.Architect);
        var artifact = new RecordEntry { Kind = "artifact", Name = "BusinessAnalysis", Content = "Completed plan" };
        var tool = new RecordEntry { Kind = "tool", Name = "CreateCommit", Content = "valid evidence", Status = "Succeeded" };
        f.Records.AddRange([artifact, tool]);
        var result = await f.Provider.ExecuteAsync(f.Context, default);
        Assert.Empty(result.Output.ToolCalls);
        Assert.Equal(2, handler.Requests);
        using var request = JsonDocument.Parse(handler.Body);
        Assert.Equal(8000, request.RootElement.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal(new[] { artifact, tool }, f.Records.Where(x => x.Kind is "artifact" or "tool"));
        var charges = await ProviderAccounting.ChargesAsync(f.Store, f.Context.Run.Id, default);
        Assert.Equal(2, charges.Length);
        Assert.Equal(4000, charges[0].Diagnostics.OutputTokens);
        Assert.Equal(8000, charges[1].Diagnostics.MaxOutputTokens);
        Assert.Equal(charges[1].Cost, result.Cost);
    }
    [Theory]
    [InlineData("budget")]
    [InlineData("context")]
    [InlineData("disabled")]
    [InlineData("ceiling")]
    public async Task OutputRecoveryCannotExceedExistingLimits(string limit)
    {
        var handler = new ModelHandler { Transform = Exhaust };
        var f = Fixture(handler, options: new() { MaxOutputRecoveryRetries = limit == "disabled" ? 0 : 1, MaxRecoveryOutputTokens = limit == "ceiling" ? 4000 : 16000 });
        var context = f.Context;
        if (limit is "context" or "budget")
        {
            // Resolve the actual protocol/schema input reserve from a successful reservation below.
            var probe = Fixture(new ModelHandler());
            await probe.Provider.ExecuteAsync(probe.Context, default);
            var reserved = JsonSerializer.Deserialize<ProviderCharge>(probe.Records.First().Content)!.Cost;
            var inputReserve = (int)((reserved - 4000 * 2m / 1_000_000m) * 1_000_000m);
            if (limit == "context") context = context with { Definition = context.Definition with { ModelContextTokens = inputReserve + 4000 } };
            else context = context with { Run = context.Run with { Policy = context.Run.Policy with { MaxBudgetUsd = reserved } } };
        }
        var error = await Assert.ThrowsAsync<ProviderException>(() => f.Provider.ExecuteAsync(context, default));
        Assert.Equal("max_output_tokens", error.Diagnostics.IncompleteReason);
        Assert.Equal(1, handler.Requests);
        Assert.DoesNotContain(f.Records, x => x.Kind == "tool");
    }
    [Fact]
    public async Task OutputRecoveryIsBoundedAndAgentAllowanceIsConfigurable()
    {
        var handler = new ModelHandler();
        handler.Transform = payload => { Exhaust(payload); payload["usage"]!["output_tokens"] = handler.Requests == 1 ? 6000 : 12000; };
        var f = Fixture(handler, AgentRole.Architect, new() { AgentOutputTokens = new() { [AgentRole.Architect] = 6000 } });
        var error = await Assert.ThrowsAsync<ProviderException>(() => f.Provider.ExecuteAsync(f.Context, default));
        Assert.Equal(2, handler.Requests);
        Assert.Equal(12000, error.Diagnostics.MaxOutputTokens);
        Assert.Equal(2, (await ProviderAccounting.ChargesAsync(f.Store, f.Context.Run.Id, default)).Length);
    }
    [Theory]
    [InlineData("prose", ProviderErrorCategory.MalformedJson, "JsonSyntax")]
    [InlineData("twoObjects", ProviderErrorCategory.MalformedJson, "JsonSyntax")]
    [InlineData("multipleMessages", ProviderErrorCategory.AmbiguousOutput, "OutputExtraction")]
    [InlineData("unrelated", ProviderErrorCategory.UnexpectedContent, "OutputExtraction")]
    [InlineData("unknownTool", ProviderErrorCategory.InvalidSchema, "SchemaValidation")]
    [InlineData("unregisteredTool", ProviderErrorCategory.InvalidToolArguments, "ToolValidation")]
    [InlineData("duplicateProperty", ProviderErrorCategory.InvalidSchema, "SchemaValidation")]
    public async Task StructuredPipelineRejectsAmbiguousOrInvalidInstructions(string scenario, ProviderErrorCategory category, string stage)
    {
        var handler = new ModelHandler { Transform = payload =>
        {
            var items = payload["output"]!.AsArray();
            if (scenario == "multipleMessages") items.Add(items[0]!.DeepClone());
            else if (scenario == "unrelated") items.Add(System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"function_call\",\"id\":\"fc_test\",\"call_id\":\"call_test\",\"name\":\"WriteFile\",\"arguments\":\"{}\",\"status\":\"completed\"}"));
            else Output(payload, output => scenario switch
            {
                "prose" => "Here is the JSON: " + output.ToJsonString(),
                "twoObjects" => output.ToJsonString() + output.ToJsonString(),
                "duplicateProperty" => output.ToJsonString().Replace("{\"summary\":", "{\"summary\":\"sensitive-data\",\"summary\":"),
                _ => SetTool(output, scenario == "unknownTool" ? "ExecuteShell" : "UnregisteredTool")
            });
        } };
        var f = Fixture(handler, options: new() { MaxMalformedResponseRetries = 0 });
        var context = f.Context;
        if (scenario == "unregisteredTool") context = context with { Definition = context.Definition with { AllowedTools = ["UnregisteredTool"] } };
        var error = await Assert.ThrowsAsync<ProviderException>(() => f.Provider.ExecuteAsync(context, default));
        Assert.Equal(category, error.Diagnostics.ErrorCategory);
        Assert.Equal(stage, error.Diagnostics.OutputStage);
        Assert.DoesNotContain("sensitive-data", JsonSerializer.Serialize(error.Diagnostics));
        Assert.Equal(1, handler.Requests);
        Assert.DoesNotContain(f.Records, x => x.Kind == "tool");
        static string SetTool(System.Text.Json.Nodes.JsonObject output, string name)
        {
            output["toolCalls"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"name\":\"" + name + "\",\"arguments\":\"{}\"}]");
            return output.ToJsonString();
        }
    }
    [Fact]
    public async Task ReasoningAndSegmentedTextUseOneCompleteAssistantMessage()
    {
        var handler = new ModelHandler { Transform = payload =>
        {
            var items = payload["output"]!.AsArray();
            var parts = items[0]!["content"]!.AsArray();
            var text = parts[0]!["text"]!.GetValue<string>();
            var second = parts[0]!.DeepClone();
            parts[0]!["text"] = text[..(text.Length / 2)];
            second["text"] = text[(text.Length / 2)..];
            parts.Add(second);
            items.Insert(0, System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"reasoning\",\"id\":\"rs_test\",\"summary\":[{\"type\":\"summary_text\",\"text\":\"unrelated sensitive reasoning\"}]}"));
        } };
        var f = Fixture(handler);
        var result = await f.Provider.ExecuteAsync(f.Context, default);
        Assert.Equal("Contract fixture", result.Output.Summary);
        Assert.Equal(2, result.Diagnostics!.OutputItemCount);
        Assert.Equal(1, result.Diagnostics.MessageCount);
        Assert.Equal(2, result.Diagnostics.TextPartCount);
        Assert.DoesNotContain("sensitive", JsonSerializer.Serialize(result.Diagnostics));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitCommentaryNeverBecomesTheStructuredDecision(bool commentaryLast)
    {
        var handler = new ModelHandler { Transform = payload =>
        {
            var items = payload["output"]!.AsArray();
            items[0]!["phase"] = "final_answer";
            var commentary = items[0]!.DeepClone();
            commentary["phase"] = "commentary";
            commentary["content"]![0]!["text"] = "sensitive progress note, not JSON or tool evidence";
            items.Insert(commentaryLast ? 1 : 0, commentary);
        } };
        var f = Fixture(handler);
        var result = await f.Provider.ExecuteAsync(f.Context, default);
        Assert.Equal("Contract fixture", result.Output.Summary);
        Assert.Empty(result.Output.ToolCalls);
        Assert.Equal(2, result.Diagnostics!.MessageCount);
        Assert.Equal(1, result.Diagnostics.TextPartCount);
        Assert.Equal("final_answer", result.Diagnostics.OutputPhase);
        Assert.DoesNotContain("sensitive", JsonSerializer.Serialize(result));
    }
    [Theory]
    [InlineData("commentary", ProviderErrorCategory.EmptyOutput)]
    [InlineData("unknown", ProviderErrorCategory.UnexpectedContent)]
    [InlineData("ambiguous", ProviderErrorCategory.AmbiguousOutput)]
    public async Task PhaseSelectionRequiresOneUnambiguousDecision(string phase, ProviderErrorCategory category)
    {
        var handler = new ModelHandler { Transform = payload =>
        {
            var items = payload["output"]!.AsArray();
            if (phase == "ambiguous")
            {
                var other = items[0]!.DeepClone();
                other["phase"] = "final_answer";
                items.Add(other);
            }
            else items[0]!["phase"] = phase;
        } };
        var f = Fixture(handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => f.Provider.ExecuteAsync(f.Context, default));
        Assert.Equal(category, error.Diagnostics.ErrorCategory);
        Assert.Equal(1, handler.Requests);
    }
    [Fact]
    public async Task SdkAggregationOfMultipleMessagesIsAmbiguousForAnAgentDecision()
    {
        var handler = new ModelHandler { Transform = payload => payload["output"]!.AsArray().Add(payload["output"]![0]!.DeepClone()) };
        var client = new OpenAI.Responses.ResponsesClient(new System.ClientModel.ApiKeyCredential("test-key"), new OpenAI.Responses.ResponsesClientOptions { Endpoint = new Uri("https://example.invalid/v1/"), Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(new HttpClient(handler)) });
        var response = await client.CreateResponseAsync(new OpenAI.Responses.CreateResponseOptions { Model = "test-model" });
        var aggregate = response.Value.GetOutputText();
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(aggregate));
        Assert.Equal(2, response.Value.OutputItems.Count);
    }
    [Fact]
    public async Task MalformedRecoveryKeepsStrictSchemaAndCheckpointsAndChargesBothRequests()
    {
        var handler = new ModelHandler();
        handler.Transform = payload => { if (handler.Requests == 1) Output(payload, output =>
        {
            output["toolCalls"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"name\":\"WriteFile\",\"arguments\":\"{\\\"path\\\":\\\"backend/rejected.cs\\\",\\\"content\\\":\\\"rejected\\\"}\"}]");
            return output.ToJsonString() + "sensitive rejected suffix";
        }); };
        var f = Fixture(handler);
        var checkpoint = new RecordEntry { Kind = "tool", Name = "WriteFile", Content = "valid checkpoint" };
        var artifact = new RecordEntry { Kind = "artifact", Content = "completed plan" };
        f.Records.AddRange([checkpoint, artifact]);
        var result = await f.Provider.ExecuteAsync(f.Context, default);
        Assert.Equal(2, handler.Requests);
        Assert.Empty(result.Output.ToolCalls);
        Assert.Equal(new[] { checkpoint, artifact }, f.Records.Where(x => x.Kind is "tool" or "artifact"));
        var charges = await ProviderAccounting.ChargesAsync(f.Store, f.Context.Run.Id, default);
        Assert.Equal(2, charges.Length);
        Assert.Equal("JsonSyntax", charges[0].Diagnostics.OutputStage);
        Assert.NotNull(charges[0].Diagnostics.JsonBytePosition);
        Assert.Equal("Validated", charges[1].Diagnostics.OutputStage);
        Assert.Equal(charges[0].Diagnostics.SchemaSha256, charges[1].Diagnostics.SchemaSha256);
        Assert.All(charges, x => Assert.Equal(4000, x.Diagnostics.MaxOutputTokens));
        Assert.DoesNotContain("sensitive", JsonSerializer.Serialize(charges));
        Assert.DoesNotContain("sensitive", handler.Body);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
    }
    [Theory]
    [InlineData("budget")]
    [InlineData("retries")]
    public async Task MalformedRecoveryStopsAtRemainingLimits(string limit)
    {
        var handler = new ModelHandler { Transform = payload => Output(payload, _ => "sensitive malformed output") };
        var f = Fixture(handler);
        var context = f.Context;
        if (limit == "budget")
        {
            var probe = Fixture(new ModelHandler());
            await probe.Provider.ExecuteAsync(probe.Context, default);
            var reserved = JsonSerializer.Deserialize<ProviderCharge>(probe.Records[0].Content)!.Cost;
            context = context with { Run = context.Run with { Policy = context.Run.Policy with { MaxBudgetUsd = reserved } } };
        }
        var error = await Assert.ThrowsAsync<ProviderException>(() => f.Provider.ExecuteAsync(context, default));
        Assert.Equal(ProviderErrorCategory.MalformedJson, error.Diagnostics.ErrorCategory);
        Assert.Equal(limit == "budget" ? 1 : 2, handler.Requests);
        Assert.DoesNotContain(f.Records, x => x.Kind == "tool");
    }
    [Theory]
    [InlineData("invalid_json_schema", "text.format.schema")]
    [InlineData("unsupported_parameter", "text.format")]
    public async Task IncompatibleModelOrSchemaNeverDowngradesStrictOutput(string code, string param)
    {
        var handler = new ModelHandler { Status = _ => HttpStatusCode.BadRequest, Transform = payload =>
        {
            payload.Clear();
            payload["error"] = new System.Text.Json.Nodes.JsonObject { ["code"] = code, ["param"] = param, ["type"] = "invalid_request_error", ["message"] = "sensitive provider explanation" };
        } };
        var f = Fixture(handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => f.Provider.ExecuteAsync(f.Context, default));
        Assert.Equal(ProviderErrorCategory.StructuredOutputIncompatible, error.Diagnostics.ErrorCategory);
        Assert.Equal(1, handler.Requests);
        Assert.DoesNotContain("sensitive", error.ToString());
        Assert.Contains("compatible", error.Diagnostics.RecoveryGuidance);
    }
    [Theory]
    [InlineData(AgentRole.BusinessAnalyst)] [InlineData(AgentRole.Architect)]
    [InlineData(AgentRole.BackendEngineer)] [InlineData(AgentRole.FrontendEngineer)]
    [InlineData(AgentRole.TestEngineer)] [InlineData(AgentRole.QAEngineer)] [InlineData(AgentRole.CodeReviewer)]
    public void StrictSchemaMatchesEntireAgentDtoContract(AgentRole role)
    {
        using var schema = JsonDocument.Parse(OpenAIProvider.SchemaFor(role, ["ReadFile"]));
        var properties = schema.RootElement.GetProperty("properties").EnumerateObject().Select(x => x.Name).Order().ToArray();
        var dto = typeof(AgentResult).GetProperties().Select(x => JsonNamingPolicy.CamelCase.ConvertName(x.Name)).Order().ToArray();
        Assert.Equal(dto, properties);
        Assert.Equal(properties, schema.RootElement.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Order());
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
    }
    private sealed class ModelHandler : HttpMessageHandler
    {
        public string Body = "";
        public int Requests;
        public Func<int, HttpStatusCode>? Status;
        public Action<System.Text.Json.Nodes.JsonObject>? Transform;
        public Func<int, CancellationToken, Task>? Before;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            if (Before != null) await Before(Requests, ct);
            Body = await request.Content!.ReadAsStringAsync(ct);
            var text = JsonSerializer.Serialize(new { summary = "Contract fixture", decision = "CONTINUE", acceptanceCriteria = new[] { "Validated contract" }, evidence = Array.Empty<string>(), assumptions = Array.Empty<string>(), toolCalls = Array.Empty<object>(), nextContext = "next", requirements = Array.Empty<object>(), findings = Array.Empty<object>(), checks = Array.Empty<object>(), openQuestions = Array.Empty<string>(), reworkTargets = Array.Empty<string>(), architectureDecisions = Array.Empty<object>(), planning = new { risks = Array.Empty<string>(), prerequisites = Array.Empty<string>(), blockers = Array.Empty<object>(), bootstrapStrategy = "WriteFile manifests", dependencyStrategy = "Verify offline cache", testStrategy = "Approved profiles" } });
            var response = new { id = "resp_contract", @object = "response", created_at = 1700000000, status = "completed", error = (object?)null, incomplete_details = (object?)null, model = "test-model", output = new[] { new { type = "message", id = "msg_contract", status = "completed", role = "assistant", content = new[] { new { type = "output_text", text, annotations = Array.Empty<object>() } } } }, usage = new { input_tokens = 100, output_tokens = 50, total_tokens = 150, input_tokens_details = new { cached_tokens = 0 }, output_tokens_details = new { reasoning_tokens = 0 } } };
            var payload = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(response))!.AsObject();
            Transform?.Invoke(payload);
            var message = new HttpResponseMessage(Status?.Invoke(Requests) ?? HttpStatusCode.OK) { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") };
            message.Headers.Add("x-request-id", "req_contract");
            return message;
        }
    }
    [Fact]
    public async Task ArtifactToolDoesNotCollideWithItsInvocationCheckpoint()
    {
        var store = NSubstitute.Substitute.For<IPlatformStore>();
        var tool = new EngineeringTool("SaveArtifact", new DeterministicSandbox(), new DeterministicSourceControl(), store);
        var definition = new AgentDefinition("a", AgentRole.Architect, "a", "", "test", "", [], ["SaveArtifact"]);
        var result = await tool.ExecuteAsync(new(new Project(), new ExecutionRun(), "", definition), JsonSerializer.SerializeToElement(new { content = "Verified design" }), "operation-id", default);
        Assert.Equal("artifact-operation-id", result.EvidenceId);
        await store.Received().AppendAsync(NSubstitute.Arg.Is<RecordEntry>(entry => entry.Id == result.EvidenceId && entry.Kind == "artifact"), default);
    }
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string Body = ""; public string IdempotencyKey = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Body = await request.Content!.ReadAsStringAsync(ct); IdempotencyKey = request.Headers.GetValues("Idempotency-Key").Single(); return new(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true,\"output\":\"test evidence\",\"evidenceId\":\"evidence\",\"exitCode\":0,\"workspaceId\":\"run\"}") }; }
    }
}
