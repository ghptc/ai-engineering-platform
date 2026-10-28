namespace AIEngineeringPlatform.Application;

public static class QualityGate
{
    public static bool Approves(AgentResult qa, ToolResult requiredTests, string[] requirementIds) =>
        requiredTests.Success && requiredTests.EvidenceId != null && requiredTests.WorkspaceRevision != null &&
        qa.Decision == "APPROVE" && qa.Evidence.Contains(requiredTests.EvidenceId) &&
        qa.Checks is { Length: > 0 } && qa.Checks.All(x => x.Passed && x.EvidenceId == requiredTests.EvidenceId) &&
        requirementIds.All(id => qa.Checks.Any(x => x.RequirementId == id));
}
