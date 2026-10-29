using AIEngineeringPlatform.Domain;
namespace AIEngineeringPlatform.Application;

public static class PlanningPolicy
{
    // Only explicit, evidence-backed future-stage blockers may be deferred.
    // Legacy or ambiguous BLOCKED outputs continue to fail closed.
    public static bool DefersExecutionBlockers(AgentRole role, AgentResult result)
    {
        if (result.Decision != "BLOCKED" || result.ToolCalls.Length != 0 || result.Planning is not { Blockers.Length: > 0 } planning) return false;
        if (planning.Blockers.Any(x => x.Stage is not ("Implementation" or "Delivery") || string.IsNullOrWhiteSpace(x.Reason) || string.IsNullOrWhiteSpace(x.Evidence))) return false;
        return role switch
        {
            AgentRole.BusinessAnalyst => result.Requirements is { Length: > 0 } requirements && requirements.All(x => !string.IsNullOrWhiteSpace(x.Title) && x.AcceptanceCriteria.Length > 0 && x.Priority is >= 1 and <= 5),
            AgentRole.Architect => result.ArchitectureDecisions is { Length: > 0 } && !string.IsNullOrWhiteSpace(planning.BootstrapStrategy) && !string.IsNullOrWhiteSpace(planning.DependencyStrategy) && !string.IsNullOrWhiteSpace(planning.TestStrategy),
            _ => false
        };
    }
}
