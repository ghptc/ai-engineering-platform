using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using System.Text.Json.Nodes;
using OpenAI.Responses;
using System.Text.Json;
using System.ClientModel;
namespace AIEngineeringPlatform.Infrastructure;

public sealed class LanguageModelOptions { public int TimeoutSeconds { get; set; } = 90; public int MaxRetries { get; set; } = 2; }
public sealed class OpenAIProvider(string? apiKey, LanguageModelOptions? settings = null, ResponsesClient? clientOverride = null, IPlatformStore? store = null) : ILanguageModelProvider
{
    public static readonly string OutputSchema = """
    {"type":"object","additionalProperties":false,"required":["summary","decision","acceptanceCriteria","evidence","assumptions","toolCalls","nextContext"],"properties":{"summary":{"type":"string"},"decision":{"type":"string","enum":["CONTINUE","APPROVE","COMMENT","REQUEST_CHANGES","BLOCKED","ALREADY_SATISFIED","AWAITING_HUMAN_INPUT"]},"acceptanceCriteria":{"type":"array","items":{"type":"string"}},"evidence":{"type":"array","description":"Exact bare evidence IDs copied from successful tool results or persisted artifact IDs. No explanatory prose or labels in this array; put explanations in summary or check details. QA must include the exact required test EvidenceId as an array item.","items":{"type":"string"}},"assumptions":{"type":"array","items":{"type":"string"}},"toolCalls":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["name","arguments"],"properties":{"name":{"type":"string"},"arguments":{"type":"string","description":"A JSON-encoded object containing tool arguments."}}}},"nextContext":{"type":"string"}}}
    """;
    public static string SchemaFor(AgentRole role, string[]? allowedTools = null)
    {
        var schema = JsonNode.Parse(OutputSchema)!.AsObject();
        var properties = schema["properties"]!.AsObject();
        var required = schema["required"]!.AsArray();
        void Array(string name, string itemSchema, string description)
        {
            properties[name] = new JsonObject { ["type"] = "array", ["description"] = description, ["items"] = JsonNode.Parse(itemSchema) };
            required.Add(name);
        }
        properties["decision"]!["description"] = "Decision for the CURRENT stage only. CONTINUE planning with future execution prerequisites or risks. BLOCKED requires a genuine current-stage impediment; missing later tests/approval is not a planning blocker. Critical business ambiguity uses AWAITING_HUMAN_INPUT.";
        properties["planning"] = JsonNode.Parse("""
        {"type":"object","additionalProperties":false,"required":["risks","prerequisites","blockers","bootstrapStrategy","dependencyStrategy","testStrategy"],"properties":{"risks":{"type":"array","items":{"type":"string"}},"prerequisites":{"type":"array","items":{"type":"string"}},"blockers":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["stage","reason","evidence"],"properties":{"stage":{"type":"string","enum":["Planning","Implementation","Delivery"]},"reason":{"type":"string"},"evidence":{"type":"string"}}}},"bootstrapStrategy":{"type":"string"},"dependencyStrategy":{"type":"string"},"testStrategy":{"type":"string"}}}
        """);
        properties["planning"]!["description"] = "BA separates risks and unverified prerequisites from confirmed blockers. Architect must supply explicit bootstrap, offline dependency verification and approved test strategies. Empty arrays/strings for other roles. Blockers require evidence and the stage they prevent; do not block planning for missing future execution evidence.";
        required.Add("planning");
        Array("requirements", """
        {"type":"object","additionalProperties":false,"required":["title","description","userStory","acceptanceCriteria","priority","dependencies","nonFunctional"],"properties":{"title":{"type":"string"},"description":{"type":"string"},"userStory":{"type":"string"},"acceptanceCriteria":{"type":"array","items":{"type":"string"}},"priority":{"type":"integer","minimum":1,"maximum":5},"dependencies":{"type":"array","items":{"type":"string"}},"nonFunctional":{"type":"boolean"}}}
        """, role == AgentRole.BusinessAnalyst ? "Required nonempty final business backlog. Include functional and nonfunctional requirements, stories, acceptance criteria and dependencies." : "Use an empty array unless proposing a requirement.");
        Array("findings", """
        {"type":"object","additionalProperties":false,"required":["severity","file","line","description","suggestedCorrection"],"properties":{"severity":{"type":"string","enum":["Critical","High","Medium","Low","Info"]},"file":{"type":"string"},"line":{"type":["integer","null"]},"description":{"type":"string"},"suggestedCorrection":{"type":"string"}}}
        """, role == AgentRole.CodeReviewer ? "Actual diff findings. Line must be null unless verified from the diff. Empty on a clean review." : "Use an empty array unless reporting a code issue.");
        Array("checks", """
        {"type":"object","additionalProperties":false,"required":["requirementId","scenario","passed","evidenceId","details"],"properties":{"requirementId":{"type":"string"},"scenario":{"type":"string"},"passed":{"type":"boolean"},"evidenceId":{"type":"string"},"details":{"type":"string"}}}
        """, role == AgentRole.QAEngineer ? "Required nonempty final QA checks mapping each acceptance criterion to an actual RunTests evidenceId." : "Use an empty array unless validating actual execution evidence.");
        Array("openQuestions", "{\"type\":\"string\"}", "Unresolved questions; label assumptions and prerequisites separately. Critical business ambiguity preventing current planning uses AWAITING_HUMAN_INPUT. An empty repository or absent current test profiles alone must not block planning.");
        Array("architectureDecisions", """
        {"type":"object","additionalProperties":false,"required":["title","context","decision","consequences","mermaid"],"properties":{"title":{"type":"string"},"context":{"type":"string"},"decision":{"type":"string"},"consequences":{"type":"string"},"mermaid":{"type":"string"}}}
        """, role == AgentRole.Architect ? "Required nonempty final architectural decisions with constraints, API contracts, auth and persistence; use Mermaid only when useful." : "Use an empty array unless proposing an ADR.");
        Array("reworkTargets", "{\"type\":\"string\",\"enum\":[\"BackendEngineer\",\"FrontendEngineer\"]}", "On rejection explicitly identify responsible selected developers from actual findings. Both means an ordered bounded correction plan. Empty if responsibility cannot be established; never guess.");
        if (allowedTools != null)
        {
            properties["toolCalls"]!["items"]!["properties"]!["name"]!["enum"] = new JsonArray((allowedTools.Length > 0 ? allowedTools : ["Unavailable"]).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            if (allowedTools.Length == 0) properties["toolCalls"]!["maxItems"] = 0;
        }
        return schema.ToJsonString();
    }
    public async Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(context.Definition.Model)) throw new ConfigurationException("OpenAI API key and agent model must be configured.");
        if (context.Definition.InputCostPerMillion <= 0 || context.Definition.OutputCostPerMillion <= 0) throw new ConfigurationException("Configure positive model token prices before executing budget-controlled requests.");
        ct.ThrowIfCancellationRequested();
        using var deadline = new CancellationTokenSource();
        var remaining = context.Run.StartedAt.AddMinutes(context.Run.Policy.MaxDurationMinutes) - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) throw new ProviderException(new(context.Definition.Model, null, null, null, null, ProviderErrorCategory.RunDeadline, null, null, 0));
        deadline.CancelAfter(remaining);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings?.TimeoutSeconds ?? 90, 5, 300)));
        var client = clientOverride ?? new ResponsesClient(new ApiKeyCredential(apiKey), new ResponsesClientOptions { RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0) });
        var options = new CreateResponseOptions
        {
            Model = context.Definition.Model,
            Instructions = context.Definition.SystemInstructions + "\nReturn only schema-valid JSON. Tool arguments are JSON-encoded objects. Allowed tools: " + string.Join(", ", context.Definition.AllowedTools) + "\nAuthoritative local tool protocol: ReadProjectMemory {kind,id,offset,limit}: offset is a nonnegative character offset, limit is 1-4000 characters (default 2000); ReadFile {path}; WriteFile {path,content}; ListFiles {}; SearchCode {query}; ApplyPatch {patch: JSON-encoded {path,search,replacement}} with exactly one matching search region; RunCommand {profile: build|lint|format|typecheck}; RunTests {profile: all|backend|frontend|e2e}; InspectDiff {}. Tool failures are feedback: correct the arguments or implementation and try a new tool call. Never claim success from a failed tool result. RunCommand file changes are temporary; use WriteFile/ApplyPatch for persistent source edits. No arbitrary shell, package-network access, image or mount selection is allowed.",
            MaxOutputTokenCount = context.Definition.MaxOutputTokens,
            StoredOutputEnabled = false,
            TextOptions = new() { TextFormat = ResponseTextFormat.CreateJsonSchemaFormat("engineering_result", BinaryData.FromString(SchemaFor(context.Definition.Role, context.Definition.AllowedTools)), jsonSchemaIsStrict: true) }
        };
        ContextBudget.Validate(context.Definition, context.Context, options.Instructions, SchemaFor(context.Definition.Role, context.Definition.AllowedTools));
        var inputReserve = (long)ContextBudget.Tokens(context.Context) + ContextBudget.Tokens(options.Instructions) + ContextBudget.Tokens(SchemaFor(context.Definition.Role, context.Definition.AllowedTools)) + 1024;
        var reserve = inputReserve * context.Definition.InputCostPerMillion / 1_000_000m + context.Definition.MaxOutputTokens * context.Definition.OutputCostPerMillion / 1_000_000m;
        if (context.Run.Cost + reserve > context.Run.Policy.MaxBudgetUsd) throw new InvalidOperationException("Insufficient remaining budget for this model request.");
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(context.Context));
        var charges = store == null ? [] : await ProviderAccounting.ChargesAsync(store, context.Run.Id, ct);
        // Run cost already includes earlier provider charges after checkpoint refresh.
        var spent = context.Run.Cost;
        var charged = 0m;
        if (store != null)
        {
            var priorTasks = new List<RecordEntry>();
            for (var offset = 0; ; offset += 1000)
            {
                var page = await store.RecordsAsync("task", null, context.Run.Id, null, 0, offset, 1000, ct);
                priorTasks.AddRange(page);
                if (page.Count < 1000) break;
            }
            var legacy = priorTasks.Select(x => JsonSerializer.Deserialize<ModelExecutionResult>(x.Content)!).Where(x => x.Diagnostics == null).Sum(x => x.Cost);
            spent = Math.Max(spent, legacy + charges.Sum(x => x.Cost));
        }
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var diagnostics = new ProviderDiagnostics(context.Definition.Model, null, null, null, null, null, null, null, attempt + 1);
            var reservationId = Guid.NewGuid().ToString("N");
            if (spent + charged + reserve > context.Run.Policy.MaxBudgetUsd)
                throw new InvalidOperationException("Insufficient remaining budget for this model request, including uncertain provider requests.");
            await RecordAsync(reservationId, diagnostics, reserve, ct, true);
            ResponseResult? response = null;
            try
            {
                var received = await client.CreateResponseAsync(options, timeout.Token);
                response = received.Value;
                received.GetRawResponse().Headers.TryGetValue("x-request-id", out var requestId);
                diagnostics = diagnostics with { ResponseId = SafeId(response.Id), RequestId = SafeId(requestId), ResponseStatus = SafeStatus(response.Status?.ToString()), IncompleteReason = SafeReason(response.IncompleteStatusDetails?.Reason.ToString()), InputTokens = response.Usage?.InputTokenCount, OutputTokens = response.Usage?.OutputTokenCount, HttpStatus = received.GetRawResponse().Status };
                if (response.IncompleteStatusDetails != null || diagnostics.ResponseStatus == "incomplete") throw Failure(ProviderErrorCategory.IncompleteResponse);
                if (response.Error != null || diagnostics.ResponseStatus == "failed") throw Failure(ProviderErrorCategory.ResponseFailed);
                if (diagnostics.ResponseStatus != "completed") throw Failure(ProviderErrorCategory.UnexpectedContent);
                using (var envelope = JsonDocument.Parse(received.GetRawResponse().Content.ToString()))
                {
                    if (!envelope.RootElement.TryGetProperty("output", out var items) || items.ValueKind != JsonValueKind.Array) throw Failure(ProviderErrorCategory.UnexpectedContent);
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("type", out var itemType)) throw Failure(ProviderErrorCategory.UnexpectedContent);
                        if (itemType.ValueKind == JsonValueKind.String && itemType.GetString() == "reasoning") continue;
                        if (itemType.ValueKind != JsonValueKind.String || itemType.GetString() != "message"
                            || !item.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() != "completed"
                            || !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array
                            || content.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Object || !x.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "output_text"))
                            throw Failure(ProviderErrorCategory.UnexpectedContent);
                    }
                }
                var text = response.GetOutputText();
                if (string.IsNullOrWhiteSpace(text)) throw Failure(ProviderErrorCategory.EmptyOutput);
                AgentResult output;
                try { output = ParseOutput(text, context.Definition); }
                catch (OutputValidationException e) { throw Failure(e.Category); }
                if (response.Usage == null || response.Usage.InputTokenCount < 0 || response.Usage.OutputTokenCount < 0) throw Failure(ProviderErrorCategory.MissingUsage);
                var input = response.Usage.InputTokenCount;
                var completion = response.Usage.OutputTokenCount;
                await RecordAsync(reservationId, diagnostics, (input * context.Definition.InputCostPerMillion + completion * context.Definition.OutputCostPerMillion) / 1_000_000m, ct);
                return new(output, response.Id, input, completion, (input * context.Definition.InputCostPerMillion + completion * context.Definition.OutputCostPerMillion) / 1_000_000m, diagnostics);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                using var recording = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await RecordAsync(reservationId, diagnostics with { ErrorCategory = ProviderErrorCategory.CallerCancellation }, reserve, recording.Token);
                throw;
            }
            catch (Exception e) when (e is OperationCanceledException or HttpRequestException or ClientResultException or ProviderException or JsonException or KeyNotFoundException or FormatException)
            {
                var category = e switch
                {
                    ProviderException p => p.Diagnostics.ErrorCategory!.Value,
                    OperationCanceledException when deadline.IsCancellationRequested => ProviderErrorCategory.RunDeadline,
                    OperationCanceledException when timeout.IsCancellationRequested => ProviderErrorCategory.RequestTimeout,
                    OperationCanceledException => ProviderErrorCategory.SdkTimeout,
                    HttpRequestException => ProviderErrorCategory.TransportFailure,
                    ClientResultException { Status: 0 } => ProviderErrorCategory.TransportFailure,
                    JsonException => ProviderErrorCategory.MalformedJson,
                    KeyNotFoundException or FormatException => ProviderErrorCategory.UnexpectedContent,
                    _ => ProviderErrorCategory.HttpFailure
                };
                diagnostics = diagnostics with { ErrorCategory = category };
                if (e is ClientResultException clientError && clientError.GetRawResponse() is { } raw)
                {
                    raw.Headers.TryGetValue("x-request-id", out var failedRequestId);
                    diagnostics = diagnostics with { RequestId = SafeId(failedRequestId), HttpStatus = clientError.Status };
                }
                var cost = response?.Usage is { InputTokenCount: >= 0, OutputTokenCount: >= 0 } usage ? (usage.InputTokenCount * context.Definition.InputCostPerMillion + usage.OutputTokenCount * context.Definition.OutputCostPerMillion) / 1_000_000m : reserve;
                // Bounded independent persistence token: caller cancellation must not erase request accounting.
                using var recording = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await RecordAsync(reservationId, diagnostics, cost, recording.Token);
                charged += cost;
                var transient = e is HttpRequestException || e is ClientResultException http && http.Status is 0 or 429 or 500 or 502 or 503 or 504;
                // Parsing, ambiguous timeouts and incomplete results require an explicit resume; no stage replay.
                if (!transient || attempt >= Math.Clamp(settings?.MaxRetries ?? 2, 0, 5) || timeout.IsCancellationRequested || store == null)
                    throw new ProviderException(diagnostics);
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), timeout.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { throw new ProviderException(diagnostics with { ErrorCategory = deadline.IsCancellationRequested ? ProviderErrorCategory.RunDeadline : ProviderErrorCategory.RequestTimeout }); }
            }
            ProviderException Failure(ProviderErrorCategory category) => new(diagnostics with { ErrorCategory = category });
        }
        async Task RecordAsync(string id, ProviderDiagnostics diagnostics, decimal cost, CancellationToken token, bool reserved = false)
        {
            if (store == null) return;
            await store.AppendAsync(new() { Id = reserved ? id : id + "-result", Kind = "providerRequest", ProjectId = context.Project.Id, RunId = context.Run.Id, Name = context.Definition.Role.ToString(), Content = JsonSerializer.Serialize(new ProviderCharge(diagnostics, cost)), Status = reserved ? "Reserved" : diagnostics.ErrorCategory?.ToString() ?? "Completed", Metadata = new() { ["model"] = context.Definition.Model, ["requestKey"] = id } }, token);
        }
    }
    private static string? SafeId(string? value) => value is { Length: <= 200 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? value : null;
    private static string? SafeStatus(string? value) => value?.ToLowerInvariant() is "completed" or "incomplete" or "failed" or "cancelled" or "queued" or "in_progress" ? value.ToLowerInvariant() : "unknown";
    private static string? SafeReason(string? value) => value?.ToLowerInvariant() is "max_output_tokens" or "content_filter" ? value.ToLowerInvariant() : value == null ? null : "unknown";

    private sealed class OutputValidationException(ProviderErrorCategory category) : Exception { public ProviderErrorCategory Category { get; } = category; }
    private static AgentResult ParseOutput(string text, AgentDefinition definition)
    {
        JsonDocument json;
        try { json = JsonDocument.Parse(text); }
        catch (JsonException) { throw new OutputValidationException(ProviderErrorCategory.MalformedJson); }
        using (json)
        {
            var root = json.RootElement;
            try
            {
                using var schema = JsonDocument.Parse(SchemaFor(definition.Role, definition.AllowedTools));
                Validate(root, schema.RootElement);
                string[] Strings(string name) => root.GetProperty(name).EnumerateArray().Select(x => x.GetString()!).ToArray();
                var calls = new List<ToolCall>();
                foreach (var call in root.GetProperty("toolCalls").EnumerateArray())
                {
                    try
                    {
                        using var args = JsonDocument.Parse(call.GetProperty("arguments").GetString()!);
                        if (args.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                        calls.Add(new(call.GetProperty("name").GetString()!, args.RootElement.Clone()));
                    }
                    catch (JsonException) { throw new OutputValidationException(ProviderErrorCategory.InvalidToolArguments); }
                }
                T? Read<T>(string name) => root.GetProperty(name).Deserialize<T>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return new(root.GetProperty("summary").GetString()!, root.GetProperty("decision").GetString()!, Strings("acceptanceCriteria"), Strings("evidence"), Strings("assumptions"), calls.ToArray(), root.GetProperty("nextContext").GetString()!, Read<RequirementProposal[]>("requirements"), Read<ReviewFinding[]>("findings"), Read<QualityCheck[]>("checks"), Strings("openQuestions"), Read<ArchitectureDecision[]>("architectureDecisions"), Strings("reworkTargets"), Read<PlanningAssessment>("planning"));
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
            { throw new OutputValidationException(ProviderErrorCategory.InvalidSchema); }
        }
    }
    // Validate the same schema sent to OpenAI before constructing application objects.
    private static void Validate(JsonElement value, JsonElement schema)
    {
        var type = value.ValueKind switch { JsonValueKind.Object => "object", JsonValueKind.Array => "array", JsonValueKind.String => "string", JsonValueKind.Number => "integer", JsonValueKind.True or JsonValueKind.False => "boolean", _ => "null" };
        var expected = schema.GetProperty("type");
        if (!(expected.ValueKind == JsonValueKind.Array ? expected.EnumerateArray().Any(x => x.GetString() == type) : expected.GetString() == type)) throw new JsonException();
        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(x => x.GetRawText() == value.GetRawText())) throw new JsonException();
        if (type == "object")
        {
            var properties = schema.GetProperty("properties");
            foreach (var name in schema.GetProperty("required").EnumerateArray()) if (!value.TryGetProperty(name.GetString()!, out _)) throw new JsonException();
            var names = new HashSet<string>();
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name) || !properties.TryGetProperty(property.Name, out var child)) throw new JsonException();
                Validate(property.Value, child);
            }
        }
        if (type == "array")
        {
            if (schema.TryGetProperty("maxItems", out var max) && value.GetArrayLength() > max.GetInt32()) throw new JsonException();
            foreach (var item in value.EnumerateArray()) Validate(item, schema.GetProperty("items"));
        }
        if (type == "integer")
        {
            if (!value.TryGetInt32(out var number) || schema.TryGetProperty("minimum", out var min) && number < min.GetInt32() || schema.TryGetProperty("maximum", out var max) && number > max.GetInt32()) throw new JsonException();
        }
    }
}
