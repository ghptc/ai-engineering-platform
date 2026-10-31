using AIEngineeringPlatform.Domain;
namespace AIEngineeringPlatform.Application;

public static class AgentOutputContract
{
    public static void Validate(AgentContext context, ModelExecutionResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Output.Summary) || result.Output.Decision is not ("CONTINUE" or "APPROVE" or "COMMENT" or "REQUEST_CHANGES" or "BLOCKED" or "ALREADY_SATISFIED" or "AWAITING_HUMAN_INPUT")) throw new InvalidOperationException("Invalid structured agent result.");
        if (result.Output.ToolCalls.Any(x => !context.Definition.AllowedTools.Contains(x.Name))) throw new InvalidOperationException("Agent requested an unauthorized tool.");
        if (result.Output.ToolCalls.Length == 0 && result.Output.Decision is not ("BLOCKED" or "AWAITING_HUMAN_INPUT"))
        {
            if (context.Definition.Role == AgentRole.BusinessAnalyst && result.Output.Requirements is not { Length: > 0 }) throw new InvalidOperationException("Business analysis requires a structured backlog.");
            if (context.Definition.Role == AgentRole.Architect && result.Output.ArchitectureDecisions is not { Length: > 0 }) throw new InvalidOperationException("Architecture requires structured decisions.");
            if (context.Definition.Role == AgentRole.QAEngineer && result.Output.Checks is not { Length: > 0 }) throw new InvalidOperationException("QA requires structured checks and evidence.");
        }
        if (result.Output.ToolCalls.Length == 0 && context.Definition.Role is AgentRole.BusinessAnalyst or AgentRole.Architect && result.Output.Decision == "CONTINUE" && result.Output.Planning == null)
            throw new InvalidOperationException("Planning requires structured risks, prerequisites and blocking conditions.");
        if (result.Output.ToolCalls.Length == 0 && context.Definition.Role == AgentRole.Architect && result.Output.Decision == "CONTINUE" && result.Output.Planning is { } planning &&
            (string.IsNullOrWhiteSpace(planning.BootstrapStrategy) || string.IsNullOrWhiteSpace(planning.DependencyStrategy) || string.IsNullOrWhiteSpace(planning.TestStrategy)))
            throw new InvalidOperationException("Architecture must address bootstrap, offline dependencies and approved tests.");
    }
}
