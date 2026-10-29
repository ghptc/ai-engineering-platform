using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using System.Text.Json.Nodes;
using OpenAI.Responses;
using System.Text.Json;
using System.ClientModel;
namespace AIEngineeringPlatform.Infrastructure;

public sealed class LanguageModelOptions { public int TimeoutSeconds { get; set; } = 90; public int MaxRetries { get; set; } = 2; }
public sealed class OpenAIProvider(string? apiKey, LanguageModelOptions? settings = null, ResponsesClient? clientOverride = null) : ILanguageModelProvider
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
        Array("requirements", """
        {"type":"object","additionalProperties":false,"required":["title","description","userStory","acceptanceCriteria","priority","dependencies","nonFunctional"],"properties":{"title":{"type":"string"},"description":{"type":"string"},"userStory":{"type":"string"},"acceptanceCriteria":{"type":"array","items":{"type":"string"}},"priority":{"type":"integer","minimum":1,"maximum":5},"dependencies":{"type":"array","items":{"type":"string"}},"nonFunctional":{"type":"boolean"}}}
        """, role == AgentRole.BusinessAnalyst ? "Required nonempty final business backlog. Include functional and nonfunctional requirements, stories, acceptance criteria and dependencies." : "Use an empty array unless proposing a requirement.");
        Array("findings", """
        {"type":"object","additionalProperties":false,"required":["severity","file","line","description","suggestedCorrection"],"properties":{"severity":{"type":"string","enum":["Critical","High","Medium","Low","Info"]},"file":{"type":"string"},"line":{"type":["integer","null"]},"description":{"type":"string"},"suggestedCorrection":{"type":"string"}}}
        """, role == AgentRole.CodeReviewer ? "Actual diff findings. Line must be null unless verified from the diff. Empty on a clean review." : "Use an empty array unless reporting a code issue.");
        Array("checks", """
        {"type":"object","additionalProperties":false,"required":["requirementId","scenario","passed","evidenceId","details"],"properties":{"requirementId":{"type":"string"},"scenario":{"type":"string"},"passed":{"type":"boolean"},"evidenceId":{"type":"string"},"details":{"type":"string"}}}
        """, role == AgentRole.QAEngineer ? "Required nonempty final QA checks mapping each acceptance criterion to an actual RunTests evidenceId." : "Use an empty array unless validating actual execution evidence.");
        Array("openQuestions", "{\"type\":\"string\"}", "Unresolved decisions. Return BLOCKED for critical unresolved business decisions.");
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings?.TimeoutSeconds ?? 90, 5, 300)));
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
        ResponseResult? response = null;
        for (var attempt = 0; ; attempt++)
        {
            try { response = await client.CreateResponseAsync(options, timeout.Token); break; }
            catch (ClientResultException e) when (attempt < Math.Clamp(settings?.MaxRetries ?? 2, 0, 5) && e.Status is 429 or 500 or 502 or 503) { await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), timeout.Token); }
            catch (ClientResultException e) { throw new ConfigurationException($"OpenAI request failed with HTTP {e.Status}. Verify the API credential, selected model, structured-output support, quota and retry configuration."); }
        }
        if (response.Error != null || response.IncompleteStatusDetails != null) throw new InvalidOperationException("Model returned an error or incomplete response.");
        using var json = JsonDocument.Parse(response.GetOutputText()); var root = json.RootElement;
        string[] Strings(string name) => root.GetProperty(name).EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        var calls = root.GetProperty("toolCalls").EnumerateArray().Select(x => new ToolCall(x.GetProperty("name").GetString()!, JsonDocument.Parse(x.GetProperty("arguments").GetString()!).RootElement.Clone())).ToArray();
        var output = new AgentResult(root.GetProperty("summary").GetString()!, root.GetProperty("decision").GetString()!, Strings("acceptanceCriteria"), Strings("evidence"), Strings("assumptions"), calls, root.GetProperty("nextContext").GetString()!, Read<RequirementProposal[]>("requirements"), Read<ReviewFinding[]>("findings"), Read<QualityCheck[]>("checks"), Strings("openQuestions"), Read<ArchitectureDecision[]>("architectureDecisions"), Strings("reworkTargets"));
        T? Read<T>(string name) => root.GetProperty(name).Deserialize<T>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var input = response.Usage?.InputTokenCount ?? throw new InvalidOperationException("Provider usage is missing.");
        var completion = response.Usage.OutputTokenCount;
        return new(output, response.Id, input, completion, (input * context.Definition.InputCostPerMillion + completion * context.Definition.OutputCostPerMillion) / 1_000_000m);
    }
}
