using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIEngineeringPlatform.Domain;

namespace AIEngineeringPlatform.Application.RuntimeV2;

public enum EngineeringMessageKind { EngineeringTask, EngineeringHandoff, ImplementationResult, TestReport, ReviewResult, ReworkRequest, ClarificationRequest, ApprovalRequest }
public enum EngineeringTaskPurpose { Planning, Implementation, Verification, Review, Research, Diagnostic, NoOp }
public sealed record ArtifactReference(string Id, string Kind, string? SourceRevision);
public sealed record EvidenceReference(string OperationId, string RecordId, string? WorkspaceRevision);
public sealed record MessageMetadata(string Stage, int Attempt, string CorrelationId);
public sealed record AgentExecutionBudget(int MaxModelRequests, int MaxToolCalls, int MaxInputTokens, int MaxOutputTokens, int TimeoutSeconds, decimal MaxCostUsd);
public sealed record EngineeringMessage(int Version, EngineeringMessageKind Kind, string MessageId, string ProjectId, string RunId, string TaskId, string Sender, string Recipient, string? SourceRevision, string Objective, string[] AcceptanceCriteria, ArtifactReference[] Artifacts, EvidenceReference[] Evidence, MessageMetadata Metadata, JsonElement Payload);
public sealed record EngineeringTaskPayload(AgentRole Role, EngineeringTaskPurpose Purpose, bool RequiresSourceChanges, string[] AuthorizedTools, AgentExecutionBudget Budget, string[] Dependencies, string ContextFrameId);
public sealed record EngineeringHandoffPayload(string Summary, string[] CompletedWork, string[] Blockers, string NextAction);
public sealed record ImplementationResultPayload(string Outcome, string[] ChangedFiles, string[] RemainingCriteria);
public sealed record TestReportPayload(string Outcome, int ExecutedCount, string[] Diagnostics);
public sealed record ReviewResultPayload(string Decision, ReviewFinding[] Findings, string[] UnverifiedCriteria);
public sealed record ReworkRequestPayload(string[] ResponsibleAgents, string Reason, string[] FailedCriteria);
public sealed record ClarificationRequestPayload(string Question, string[] Options, bool BlocksCurrentTask);
public sealed record ApprovalRequestPayload(string Gate, string DecisionArtifactId, string RequestedOwnerAction);

public static class EngineeringMessageContract
{
    public const int Version = 1;
    public const int MaximumBytes = 65536;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, MaxDepth = 24,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public static EngineeringMessage Parse(string json, string authorizedSender, IReadOnlySet<string> authorizedRecipients)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            if (bytes.Length > MaximumBytes) throw Invalid();
            RejectDuplicates(bytes);
            var message = JsonSerializer.Deserialize<EngineeringMessage>(bytes, Json) ?? throw Invalid();
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.GetProperty("kind").GetString() != message.Kind.ToString()) throw Invalid();
            Validate(message, authorizedSender, authorizedRecipients);
            return message;
        }
        catch (Exception error) when (error is JsonException or FormatException or NullReferenceException or ArgumentException)
        { throw Invalid(); }
    }
    public static T Payload<T>(EngineeringMessage message) where T : class => message.Payload.Deserialize<T>(Json) ?? throw Invalid();
    public static string Serialize(EngineeringMessage message) => JsonSerializer.Serialize(message, Json);
    public static void Validate(EngineeringMessage message, string authorizedSender, IReadOnlySet<string> recipients)
    {
        if (message.Version != Version || !Enum.IsDefined(message.Kind) || message.Sender != authorizedSender || !recipients.Contains(message.Recipient) ||
            !Id(message.MessageId) || !Id(message.ProjectId) || !Id(message.RunId) || !Id(message.TaskId) || !Revision(message.SourceRevision) ||
            !Text(message.Objective, 4000) || !Strings(message.AcceptanceCriteria, 1, 50, 2000) ||
            message.Artifacts.Length > 64 || message.Evidence.Length > 64 || message.Metadata.Attempt is < 0 or > 1000 ||
            !Text(message.Metadata.Stage, 80) || !Id(message.Metadata.CorrelationId)) throw Invalid();
        if (message.Artifacts.Any(x => !Id(x.Id) || x.Kind is not ("artifact" or "tool" or "task" or "requirement" or "handoff" or "message" or "providerRequest") || !Revision(x.SourceRevision)) ||
            message.Evidence.Any(x => !Id(x.OperationId) || !Id(x.RecordId) || !Revision(x.WorkspaceRevision)) ||
            message.Evidence.Select(x => x.OperationId).Distinct().Count() != message.Evidence.Length) throw Invalid();
        if (message.Kind == EngineeringMessageKind.EngineeringTask)
        {
            if (authorizedSender != "Orchestrator") throw Invalid();
            var task = Payload<EngineeringTaskPayload>(message);
            if (message.Payload.GetProperty("role").GetString() != task.Role.ToString() || message.Payload.GetProperty("purpose").GetString() != task.Purpose.ToString() || !Enum.IsDefined(task.Role) || message.Recipient != task.Role.ToString() || !Enum.IsDefined(task.Purpose) || !Id(task.ContextFrameId) ||
                !Strings(task.Dependencies, 0, 32, 128) || !Strings(task.AuthorizedTools, 0, 32, 80) || task.AuthorizedTools.Distinct().Count() != task.AuthorizedTools.Length ||
                task.RequiresSourceChanges && task.Purpose is not EngineeringTaskPurpose.Implementation) throw Invalid();
            ValidateBudget(task.Budget);
            return;
        }
        if (message.Recipient != "Orchestrator" && message.Kind != EngineeringMessageKind.ApprovalRequest) throw Invalid();
        if (message.Kind != EngineeringMessageKind.ApprovalRequest && !Enum.GetNames<AgentRole>().Contains(authorizedSender)) throw Invalid();
        switch (message.Kind)
        {
            case EngineeringMessageKind.EngineeringHandoff:
                var handoff = Payload<EngineeringHandoffPayload>(message);
                if (!Text(handoff.Summary, 4000) || !Text(handoff.NextAction, 4000) || !Strings(handoff.CompletedWork, 0, 50, 2000) || !Strings(handoff.Blockers, 0, 20, 2000)) throw Invalid();
                break;
            case EngineeringMessageKind.ImplementationResult:
                var implementation = Payload<ImplementationResultPayload>(message);
                if (authorizedSender is not ("BackendEngineer" or "FrontendEngineer" or "InfrastructureEngineer") || implementation.Outcome is not ("ProposedComplete" or "Blocked" or "NoOp") || !Strings(implementation.ChangedFiles, 0, 100, 300) || !Strings(implementation.RemainingCriteria, 0, 50, 2000) || implementation.Outcome == "ProposedComplete" && message.SourceRevision == null) throw Invalid();
                break;
            case EngineeringMessageKind.TestReport:
                var report = Payload<TestReportPayload>(message);
                if (authorizedSender != "TestEngineer" || report.Outcome is not ("Passed" or "Failed" or "Unavailable") || report.ExecutedCount is < 0 or > 100000 || !Strings(report.Diagnostics, 0, 50, 2000) || report.Outcome == "Passed" && (report.ExecutedCount == 0 || message.SourceRevision == null || message.Evidence.Length == 0)) throw Invalid();
                break;
            case EngineeringMessageKind.ReviewResult:
                var review = Payload<ReviewResultPayload>(message);
                if (authorizedSender is not ("QAEngineer" or "CodeReviewer" or "SecurityEngineer") || review.Decision is not ("Approve" or "RequestChanges" or "Unverified") || review.Findings.Length > 100 || !Strings(review.UnverifiedCriteria, 0, 50, 2000) || review.Findings.Any(x => !Text(x.Description, 4000) || !Text(x.File, 300) || !Text(x.SuggestedCorrection, 4000) || x.Line is < 1 || x.Severity is not ("Critical" or "High" or "Medium" or "Low" or "Info"))) throw Invalid();
                break;
            case EngineeringMessageKind.ReworkRequest:
                var rework = Payload<ReworkRequestPayload>(message);
                if (authorizedSender is not ("TestEngineer" or "QAEngineer" or "CodeReviewer" or "SecurityEngineer") || !Strings(rework.ResponsibleAgents, 1, 3, 80) || rework.ResponsibleAgents.Any(x => x is not ("BackendEngineer" or "FrontendEngineer" or "InfrastructureEngineer")) || !Text(rework.Reason, 4000) || !Strings(rework.FailedCriteria, 1, 50, 2000) || message.Evidence.Length == 0) throw Invalid();
                break;
            case EngineeringMessageKind.ClarificationRequest:
                var clarification = Payload<ClarificationRequestPayload>(message);
                if (!Text(clarification.Question, 4000) || !Strings(clarification.Options, 0, 10, 1000)) throw Invalid();
                break;
            case EngineeringMessageKind.ApprovalRequest:
                var approval = Payload<ApprovalRequestPayload>(message);
                if (authorizedSender != "Orchestrator" || message.Recipient != "Owner" || approval.Gate is not ("Delivery" or "Security" or "ExecutionProfile") || !Id(approval.DecisionArtifactId) || !Text(approval.RequestedOwnerAction, 4000)) throw Invalid();
                break;
            default: throw Invalid();
        }
    }
    public static void ValidateBudget(AgentExecutionBudget budget)
    {
        if (budget.MaxModelRequests is < 1 or > 10 || budget.MaxToolCalls is < 0 or > 1000 || budget.MaxInputTokens is < 1 or > 1000000 || budget.MaxOutputTokens is < 100 or > 16000 || budget.TimeoutSeconds is < 1 or > 300 || budget.MaxCostUsd is <= 0 or > 1000) throw Invalid();
    }
    public static JsonNode Schema(EngineeringMessageKind kind)
    {
        if (!Enum.IsDefined(kind)) throw Invalid();
        var schema = Json.GetJsonSchemaAsNode(typeof(EngineeringMessage));
        schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        schema["$id"] = "urn:aep:engineering-message:v1:" + kind;
        schema["properties"]!["version"]!["const"] = Version;
        schema["properties"]!["kind"]!["const"] = kind.ToString();
        var type = kind switch
        {
            EngineeringMessageKind.EngineeringTask => typeof(EngineeringTaskPayload), EngineeringMessageKind.EngineeringHandoff => typeof(EngineeringHandoffPayload),
            EngineeringMessageKind.ImplementationResult => typeof(ImplementationResultPayload), EngineeringMessageKind.TestReport => typeof(TestReportPayload),
            EngineeringMessageKind.ReviewResult => typeof(ReviewResultPayload), EngineeringMessageKind.ReworkRequest => typeof(ReworkRequestPayload),
            EngineeringMessageKind.ClarificationRequest => typeof(ClarificationRequestPayload), _ => typeof(ApprovalRequestPayload)
        };
        schema["properties"]!["payload"] = Json.GetJsonSchemaAsNode(type);
        schema["properties"]!["objective"]!["maxLength"] = 4000;
        schema["properties"]!["acceptanceCriteria"]!["maxItems"] = 50;
        schema["properties"]!["acceptanceCriteria"]!["minItems"] = 1;
        schema["properties"]!["artifacts"]!["maxItems"] = 64;
        schema["properties"]!["evidence"]!["maxItems"] = 64;
        var properties = schema["properties"]!;
        foreach (var name in new[] { "messageId", "projectId", "runId", "taskId", "sender", "recipient" }) { properties[name]!["maxLength"] = 128; properties[name]!["minLength"] = 1; }
        properties["objective"]!["minLength"] = 1;
        properties["acceptanceCriteria"]!["items"]!["maxLength"] = 2000;
        properties["acceptanceCriteria"]!["items"]!["minLength"] = 1;
        properties["metadata"]!["properties"]!["attempt"]!["minimum"] = 0;
        properties["metadata"]!["properties"]!["attempt"]!["maximum"] = 1000;
        properties["metadata"]!["properties"]!["stage"]!["maxLength"] = 80;
        properties["metadata"]!["properties"]!["correlationId"]!["maxLength"] = 128;
        var payload = properties["payload"]!["properties"]!;
        static JsonArray Values(params string[] values) => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        switch (kind)
        {
            case EngineeringMessageKind.EngineeringTask:
                properties["sender"]!["const"] = "Orchestrator";
                var budget = payload["budget"]!["properties"]!;
                foreach (var name in new[] { "maxModelRequests", "maxInputTokens", "timeoutSeconds" }) budget[name]!["minimum"] = 1;
                budget["maxModelRequests"]!["maximum"] = 10; budget["maxToolCalls"]!["minimum"] = 0; budget["maxToolCalls"]!["maximum"] = 1000;
                budget["maxInputTokens"]!["maximum"] = 1000000; budget["maxOutputTokens"]!["minimum"] = 100; budget["maxOutputTokens"]!["maximum"] = 16000;
                budget["timeoutSeconds"]!["maximum"] = 300; budget["maxCostUsd"]!["exclusiveMinimum"] = 0; budget["maxCostUsd"]!["maximum"] = 1000;
                payload["authorizedTools"]!["maxItems"] = 32; payload["authorizedTools"]!["uniqueItems"] = true; payload["dependencies"]!["maxItems"] = 32;
                break;
            case EngineeringMessageKind.ImplementationResult: payload["outcome"]!["enum"] = Values("ProposedComplete", "Blocked", "NoOp"); break;
            case EngineeringMessageKind.TestReport:
                payload["outcome"]!["enum"] = Values("Passed", "Failed", "Unavailable"); payload["executedCount"]!["minimum"] = 0; payload["executedCount"]!["maximum"] = 100000;
                break;
            case EngineeringMessageKind.ReviewResult: payload["decision"]!["enum"] = Values("Approve", "RequestChanges", "Unverified"); payload["findings"]!["maxItems"] = 100; break;
            case EngineeringMessageKind.ReworkRequest: payload["responsibleAgents"]!["minItems"] = 1; payload["responsibleAgents"]!["maxItems"] = 3; payload["failedCriteria"]!["minItems"] = 1; break;
            case EngineeringMessageKind.ApprovalRequest:
                properties["sender"]!["const"] = "Orchestrator"; properties["recipient"]!["const"] = "Owner"; payload["gate"]!["enum"] = Values("Delivery", "Security", "ExecutionProfile"); break;
        }
        return schema;
    }
    private static bool Text(string value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
    private static bool Id(string value) => value is { Length: > 0 and <= 128 } && Regex.IsMatch(value, @"\A[A-Za-z0-9_.:/-]+\z");
    private static bool Revision(string? value) => value == null || value.Length is 40 or 64 && value.All(char.IsAsciiHexDigit);
    private static bool Strings(string[] values, int minimum, int maximum, int length) => values != null && values.Length >= minimum && values.Length <= maximum && values.All(x => Text(x, length));
    private static ArgumentException Invalid() => new("Engineering message violates its versioned schema, bounds or sender/recipient authority.");
    private static void RejectDuplicates(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 24 });
        var stack = new Stack<HashSet<string>?>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) stack.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.StartArray) stack.Push(null);
            else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) stack.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !stack.Peek()!.Add(reader.GetString()!)) throw Invalid();
        }
    }
}
