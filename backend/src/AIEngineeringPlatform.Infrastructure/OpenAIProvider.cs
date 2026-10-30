using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using System.Text.Json.Nodes;
using OpenAI.Responses;
using System.Text.Json;
using System.ClientModel;
namespace AIEngineeringPlatform.Infrastructure;

public sealed class LanguageModelOptions
{
    public int TimeoutSeconds { get; set; } = 90;
    public int MaxRetries { get; set; } = 2;
    public int MaxMalformedResponseRetries { get; set; } = 1;
    public int MaxOutputRecoveryRetries { get; set; } = 1;
    public int MaxRecoveryOutputTokens { get; set; } = 16000;
    public Dictionary<AgentRole, int> AgentOutputTokens { get; set; } = new();
}
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
        if (settings?.AgentOutputTokens.TryGetValue(context.Definition.Role, out var agentOutput) == true)
            context = context with { Definition = context.Definition with { MaxOutputTokens = agentOutput } };
        using var deadline = new CancellationTokenSource();
        var remaining = context.Run.StartedAt.AddMinutes(context.Run.Policy.MaxDurationMinutes) - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) throw new ProviderException(new(context.Definition.Model, null, null, null, null, ProviderErrorCategory.RunDeadline, null, null, 0));
        deadline.CancelAfter(remaining);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings?.TimeoutSeconds ?? 90, 5, 300)));
        var client = clientOverride ?? new ResponsesClient(new ApiKeyCredential(apiKey), new ResponsesClientOptions { RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0) });
        var schema = SchemaFor(context.Definition.Role, context.Definition.AllowedTools);
        var schemaHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(schema))).ToLowerInvariant();
        var options = new CreateResponseOptions
        {
            Model = context.Definition.Model,
            Instructions = context.Definition.SystemInstructions + "\nReturn only concise schema-valid JSON. Intermediate tool requests should leave final planning arrays empty; produce the final plan after consuming evidence. Do not repeat requirements, tool results or the same plan across summary, nextContext and architecture decisions. Tool arguments are JSON-encoded objects. Allowed tools: " + string.Join(", ", context.Definition.AllowedTools) + "\nAuthoritative local tool protocol: ReadProjectMemory {kind,id,offset,limit}: offset is a nonnegative character offset, limit is 1-4000 characters (default 2000); ReadFile {path}; WriteFile {path,content}; ListFiles {}; SearchCode {query}; ApplyPatch {patch: JSON-encoded {path,search,replacement}} with exactly one matching search region; RunCommand {profile: build|lint|format|typecheck}; RunTests {profile: all|backend|frontend|e2e}; InspectDiff {}. Tool failures are feedback: correct the arguments or implementation and try a new tool call. Never claim success from a failed tool result. RunCommand file changes are temporary; use WriteFile/ApplyPatch for persistent source edits. No arbitrary shell, package-network access, image or mount selection is allowed.",
            MaxOutputTokenCount = context.Definition.MaxOutputTokens,
            StoredOutputEnabled = false,
            TextOptions = new() { TextFormat = ResponseTextFormat.CreateJsonSchemaFormat("engineering_result", BinaryData.FromString(schema), jsonSchemaIsStrict: true) }
        };
        ContextBudget.Validate(context.Definition, context.Context, options.Instructions, schema);
        var inputReserve = (long)ContextBudget.Tokens(context.Context) + ContextBudget.Tokens(options.Instructions) + ContextBudget.Tokens(schema) + 1024;
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
        var outputRecoveries = 0;
        var syntaxRecoveries = 0;
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var diagnostics = new ProviderDiagnostics(context.Definition.Model, null, null, null, null, null, null, null, attempt + 1, MaxOutputTokens: options.MaxOutputTokenCount, OutputStage: "ResponseEnvelope", SchemaSha256: schemaHash, OutputFormat: "json_schema_strict");
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
                diagnostics = diagnostics with { OutputStage = "OutputExtraction" };
                var text = ExtractOutput(received.GetRawResponse().Content, ref diagnostics);
                if (string.IsNullOrWhiteSpace(text)) throw Failure(ProviderErrorCategory.EmptyOutput);
                AgentResult output;
                try { output = ParseOutput(text, context.Definition, schema, ref diagnostics); }
                catch (OutputValidationException e) { throw Failure(e.Category); }
                diagnostics = diagnostics with { OutputStage = "UsageValidation" };
                if (response.Usage == null || response.Usage.InputTokenCount < 0 || response.Usage.OutputTokenCount < 0) throw Failure(ProviderErrorCategory.MissingUsage);
                var input = response.Usage.InputTokenCount;
                var completion = response.Usage.OutputTokenCount;
                diagnostics = diagnostics with { OutputStage = "Validated" };
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
                    if (IsStructuredOutputIncompatible(clientError.Status, raw.Content)) category = ProviderErrorCategory.StructuredOutputIncompatible;
                    diagnostics = diagnostics with { RequestId = SafeId(failedRequestId), HttpStatus = clientError.Status, ErrorCategory = category };
                }
                var cost = response?.Usage is { InputTokenCount: >= 0, OutputTokenCount: >= 0 } usage ? (usage.InputTokenCount * context.Definition.InputCostPerMillion + usage.OutputTokenCount * context.Definition.OutputCostPerMillion) / 1_000_000m : reserve;
                // Bounded independent persistence token: caller cancellation must not erase request accounting.
                using var recording = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await RecordAsync(reservationId, diagnostics, cost, recording.Token);
                charged += cost;
                // Repeat the same strict request, without feeding rejected text back into context.
                // Fully settled charges and uncertain reservations count against the next allowance.
                if (category == ProviderErrorCategory.MalformedJson && diagnostics.OutputStage == "JsonSyntax"
                    && syntaxRecoveries < Math.Clamp(settings?.MaxMalformedResponseRetries ?? 1, 0, 2)
                    && attempt < Math.Min(context.Run.Policy.MaxRetries, Math.Clamp(settings?.MaxRetries ?? 2, 0, 5))
                    && spent + charged + reserve <= context.Run.Policy.MaxBudgetUsd
                    && store != null && !timeout.IsCancellationRequested)
                {
                    syntaxRecoveries++;
                    continue;
                }
                // A rejected response has produced no executable application instructions. Retry only
                // this model request, never a stage or tool batch, with a fresh accounted reservation.
                if (category == ProviderErrorCategory.IncompleteResponse && diagnostics.IncompleteReason == "max_output_tokens"
                    && diagnostics.OutputTokens >= options.MaxOutputTokenCount
                    && attempt < context.Run.Policy.MaxRetries
                    && outputRecoveries < Math.Min(Math.Clamp(settings?.MaxOutputRecoveryRetries ?? 1, 0, 2), context.Run.Policy.MaxRetries)
                    && store != null && !timeout.IsCancellationRequested)
                {
                    var current = options.MaxOutputTokenCount!.Value;
                    var ceiling = Math.Min((long)(settings?.MaxRecoveryOutputTokens ?? 16000), context.Definition.ModelContextTokens - inputReserve);
                    var affordable = decimal.Floor((context.Run.Policy.MaxBudgetUsd - spent - charged - inputReserve * context.Definition.InputCostPerMillion / 1_000_000m) * 1_000_000m / context.Definition.OutputCostPerMillion);
                    var next = (int)Math.Max(0, Math.Min(Math.Min((long)current * 2, ceiling), Math.Min(affordable, int.MaxValue)));
                    if (next > current)
                    {
                        options.MaxOutputTokenCount = next;
                        reserve = inputReserve * context.Definition.InputCostPerMillion / 1_000_000m + next * context.Definition.OutputCostPerMillion / 1_000_000m;
                        outputRecoveries++;
                        continue;
                    }
                }
                var transient = e is HttpRequestException || e is ClientResultException http && http.Status is 0 or 429 or 500 or 502 or 503 or 504;
                // Schema/tool/extraction failures, ambiguous timeouts and other incomplete reasons require inspection.
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
    private static string? SafeReason(string? value) => value?.ToLowerInvariant() is "max_output_tokens" or "content_filter" or "steered" ? value.ToLowerInvariant() : value == null ? null : "unknown";

    private static string ExtractOutput(BinaryData content, ref ProviderDiagnostics diagnostics)
    {
        using var envelope = JsonDocument.Parse(content.ToString());
        if (envelope.RootElement.ValueKind != JsonValueKind.Object || !envelope.RootElement.TryGetProperty("output", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new ProviderException(diagnostics with { ErrorCategory = ProviderErrorCategory.UnexpectedContent });
        var messages = 0;
        var parts = 0;
        var decisions = 0;
        var text = new System.Text.StringBuilder();
        diagnostics = diagnostics with { OutputItemCount = items.GetArrayLength(), MessageCount = 0, TextPartCount = 0 };
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                throw new ProviderException(diagnostics with { ErrorCategory = ProviderErrorCategory.UnexpectedContent });
            if (type.GetString() == "reasoning") continue;
            if (type.GetString() != "message"
                || !item.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String || role.GetString() != "assistant"
                || !item.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() != "completed"
                || !item.TryGetProperty("content", out var contents) || contents.ValueKind != JsonValueKind.Array)
                throw new ProviderException(diagnostics with { ErrorCategory = ProviderErrorCategory.UnexpectedContent });
            messages++;
            diagnostics = diagnostics with { MessageCount = messages };
            string? phase = null;
            if (item.TryGetProperty("phase", out var phaseValue) && phaseValue.ValueKind != JsonValueKind.Null)
            {
                if (phaseValue.ValueKind != JsonValueKind.String || phaseValue.GetString() is not ("commentary" or "final_answer"))
                    throw new ProviderException(diagnostics with { ErrorCategory = ProviderErrorCategory.UnexpectedContent });
                phase = phaseValue.GetString();
            }
            var isDecision = phase != "commentary";
            if (isDecision)
            {
                if (++decisions > 1) throw new ProviderException(diagnostics with { ErrorCategory = ProviderErrorCategory.AmbiguousOutput });
                diagnostics = diagnostics with { OutputPhase = phase ?? "unphased" };
            }
            foreach (var part in contents.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("type", out var partType) || partType.ValueKind != JsonValueKind.String || partType.GetString() != "output_text"
                    || !part.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String)
                    throw new ProviderException(diagnostics with { ErrorCategory = ProviderErrorCategory.UnexpectedContent });
                if (!isDecision) continue;
                parts++;
                text.Append(value.GetString());
                diagnostics = diagnostics with { TextPartCount = parts, OutputTextCharacters = text.Length };
            }
        }
        return text.ToString();
    }
    private static bool IsStructuredOutputIncompatible(int status, BinaryData? content)
    {
        if (status is not (400 or 422) || content == null) return false;
        try
        {
            using var json = JsonDocument.Parse(content.ToString());
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return false;
            var code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var param = error.TryGetProperty("param", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            return code == "invalid_json_schema" || param is "text.format" or "text.format.type" or "text.format.schema" or "text.format.strict" or "response_format";
        }
        catch (JsonException) { return false; }
    }
    private sealed class OutputValidationException(ProviderErrorCategory category) : Exception { public ProviderErrorCategory Category { get; } = category; }
    private static AgentResult ParseOutput(string text, AgentDefinition definition, string outputSchema, ref ProviderDiagnostics diagnostics)
    {
        JsonDocument json;
        diagnostics = diagnostics with { OutputStage = "JsonSyntax" };
        try { json = JsonDocument.Parse(text); }
        catch (JsonException e)
        {
            diagnostics = diagnostics with { JsonLineNumber = e.LineNumber, JsonBytePosition = e.BytePositionInLine };
            throw new OutputValidationException(ProviderErrorCategory.MalformedJson);
        }
        using (json)
        {
            var root = json.RootElement;
            try
            {
                diagnostics = diagnostics with { OutputStage = "SchemaValidation" };
                using var schema = JsonDocument.Parse(outputSchema);
                Validate(root, schema.RootElement);
                string[] Strings(string name) => root.GetProperty(name).EnumerateArray().Select(x => x.GetString()!).ToArray();
                diagnostics = diagnostics with { OutputStage = "ToolValidation" };
                var calls = new List<ToolCall>();
                foreach (var call in root.GetProperty("toolCalls").EnumerateArray())
                {
                    try
                    {
                        var name = call.GetProperty("name").GetString()!;
                        if (!definition.AllowedTools.Contains(name, StringComparer.Ordinal) || !EngineeringTool.Names.Contains(name, StringComparer.Ordinal))
                            throw new OutputValidationException(ProviderErrorCategory.InvalidToolArguments);
                        var arguments = call.GetProperty("arguments").GetString()!;
                        if (arguments.Length > 100000) throw new OutputValidationException(ProviderErrorCategory.InvalidToolArguments);
                        using var args = JsonDocument.Parse(arguments);
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
