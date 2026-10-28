using AIEngineeringPlatform.Application;
using Xunit;
namespace AIEngineeringPlatform.Application.Tests;
public class QualityGateTests
{
    [Fact]
    public void QaApprovalRequiresExactRequiredEvidenceIdsRatherThanProseClaims()
    {
        var tests = new ToolResult(true, "1 passed", "test-1", "revision-1");
        var qa = new AgentResult("Validated", "APPROVE", [], ["test-1"], [], [], "", Checks: [new("r", "Acceptance", true, "test-1", "Actual test output inspected")]);
        Assert.True(QualityGate.Approves(qa, tests, ["r"]));
        Assert.False(QualityGate.Approves(qa with { Evidence = ["Retrieved test-1: passed"] }, tests, ["r"]));
        Assert.False(QualityGate.Approves(qa with { Checks = [new("r", "Acceptance", true, "other-test", "Wrong execution")] }, tests, ["r"]));
        Assert.False(QualityGate.Approves(qa, tests, ["missing-requirement"]));
        Assert.False(QualityGate.Approves(qa, tests with { WorkspaceRevision = null }, ["r"]));
    }
}
