using AIEngineeringPlatform.Application.RuntimeV2;
using AIEngineeringPlatform.Domain;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace AIEngineeringPlatform.Application.Tests;

public sealed class RuntimeMessageContractTests
{
    private static readonly string Revision = new('a', 64);
    public static EngineeringMessage Message(EngineeringMessageKind kind, object payload, string sender, string recipient = "Orchestrator") => new(1, kind, "message-one", "project-one", "run-one", "task-one", sender, recipient, Revision, "Implement the bounded task", ["Acceptance criterion"], [], [], new("Implementing", 0, "correlation-one"), JsonSerializer.SerializeToElement(payload, EngineeringMessageContract.Json));
    public static EngineeringTaskPayload TaskPayload() => new(AgentRole.BackendEngineer, EngineeringTaskPurpose.Implementation, true, ["ReadRepository", "WriteFile"], new(2, 2, 40000, 1000, 30, 1), [], "context-one");
    [Fact] public void EveryRequiredMessageKindHasAStrictSchemaAndRoundTrips()
    {
        var messages = new[]
        {
            Message(EngineeringMessageKind.EngineeringTask, TaskPayload(), "Orchestrator", "BackendEngineer"),
            Message(EngineeringMessageKind.EngineeringHandoff, new EngineeringHandoffPayload("Summary", ["Actual work"], [], "Next bounded task"), "BackendEngineer"),
            Message(EngineeringMessageKind.ImplementationResult, new ImplementationResultPayload("ProposedComplete", ["backend/Api.cs"], []), "BackendEngineer"),
            Message(EngineeringMessageKind.TestReport, new TestReportPayload("Failed", 0, ["No executable tests yet"]), "TestEngineer"),
            Message(EngineeringMessageKind.ReviewResult, new ReviewResultPayload("Unverified", [], ["Missing tests"]), "CodeReviewer"),
            Message(EngineeringMessageKind.ReworkRequest, new ReworkRequestPayload(["BackendEngineer"], "A verified test failed", ["Endpoint behavior"]), "QAEngineer") with { Evidence = [new("operation-one", "operation-one", Revision)] },
            Message(EngineeringMessageKind.ClarificationRequest, new ClarificationRequestPayload("Which scope is authorized?", ["Local", "Remote"], true), "Architect"),
            Message(EngineeringMessageKind.ApprovalRequest, new ApprovalRequestPayload("Delivery", "decision-artifact", "Review the draft delivery"), "Orchestrator", "Owner")
        };
        Assert.Equal(8, messages.Length);
        foreach (var message in messages)
        {
            var result = EngineeringMessageContract.Parse(EngineeringMessageContract.Serialize(message), message.Sender, new HashSet<string> { message.Recipient });
            Assert.Equal(message.MessageId, result.MessageId);
            var schema = EngineeringMessageContract.Schema(message.Kind);
            Assert.Equal(message.Kind.ToString(), schema["properties"]!["kind"]!["const"]!.GetValue<string>());
            Assert.False(schema["additionalProperties"]!.GetValue<bool>());
            Assert.False(schema["properties"]!["payload"]!["additionalProperties"]!.GetValue<bool>());
        }
    }
    [Theory] [InlineData("unknown")] [InlineData("missing")] [InlineData("duplicate")] [InlineData("version")] [InlineData("sender")] [InlineData("recipient")] [InlineData("numeric-enum")] [InlineData("lowercase-enum")] [InlineData("payload-extra")] [InlineData("string-number")] [InlineData("lowercase-role")] [InlineData("lowercase-purpose")] [InlineData("null-objective")]
    public void InvalidOrUnauthorizedMessagesFailClosed(string mutation)
    {
        var message = Message(EngineeringMessageKind.EngineeringTask, TaskPayload(), "Orchestrator", "BackendEngineer");
        var node = JsonNode.Parse(EngineeringMessageContract.Serialize(message))!;
        if (mutation == "unknown") node["unexpected"] = true;
        if (mutation == "missing") node.AsObject().Remove("objective");
        if (mutation == "version") node["version"] = 2;
        if (mutation == "sender") node["sender"] = "BackendEngineer";
        if (mutation == "recipient") node["recipient"] = "Owner";
        if (mutation == "numeric-enum") node["kind"] = 0;
        if (mutation == "lowercase-enum") node["kind"] = "engineeringtask";
        if (mutation == "string-number") node["version"] = "1";
        if (mutation == "lowercase-role") node["payload"]!["role"] = "backendengineer";
        if (mutation == "lowercase-purpose") node["payload"]!["purpose"] = "implementation";
        if (mutation == "null-objective") node["objective"] = null;
        if (mutation == "payload-extra") node["payload"]!["grantHostAccess"] = true;
        var json = node.ToJsonString();
        if (mutation == "duplicate") json = json.Replace("\"version\":1", "\"version\":1,\"version\":1");
        Assert.Throws<ArgumentException>(() => EngineeringMessageContract.Parse(json, "Orchestrator", new HashSet<string> { "BackendEngineer" }));
    }
    [Fact] public void ZeroTestsAndModelAuthoredApprovalGrantsAreNotAccepted()
    {
        var report = Message(EngineeringMessageKind.TestReport, new TestReportPayload("Passed", 0, []), "TestEngineer") with { Evidence = [new("operation-one", "operation-one", Revision)] };
        Assert.Throws<ArgumentException>(() => EngineeringMessageContract.Parse(EngineeringMessageContract.Serialize(report), "TestEngineer", new HashSet<string> { "Orchestrator" }));
        var grant = Message(EngineeringMessageKind.ApprovalRequest, new ApprovalRequestPayload("Delivery", "artifact", "Approve me"), "BackendEngineer", "Owner");
        Assert.Throws<ArgumentException>(() => EngineeringMessageContract.Parse(EngineeringMessageContract.Serialize(grant), "BackendEngineer", new HashSet<string> { "Owner" }));
    }
    [Fact] public void OversizedPayloadAndDuplicateEvidenceCannotInflateCompletion()
    {
        var message = Message(EngineeringMessageKind.ClarificationRequest, new ClarificationRequestPayload(new string('x', 70000), [], true), "Architect");
        Assert.Throws<ArgumentException>(() => EngineeringMessageContract.Parse(EngineeringMessageContract.Serialize(message), "Architect", new HashSet<string> { "Orchestrator" }));
        message = message with { Payload = JsonSerializer.SerializeToElement(new ClarificationRequestPayload("Question", [], true), EngineeringMessageContract.Json), Evidence = [new("same", "same", Revision), new("same", "same", Revision)] };
        Assert.Throws<ArgumentException>(() => EngineeringMessageContract.Parse(EngineeringMessageContract.Serialize(message), "Architect", new HashSet<string> { "Orchestrator" }));
    }
}
