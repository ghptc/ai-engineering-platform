using AIEngineeringPlatform.Domain;
namespace AIEngineeringPlatform.Application;

public static class ReworkPlanner
{
    public static ReworkTransition[] Create(ExecutionRun run, AgentRole source, AgentResult result, string? revision)
    {
        if (run.Iteration >= run.Policy.MaxIterations) throw new InvalidOperationException("Maximum development iterations reached.");
        var developers = run.SelectedAgents.Where(x => x is AgentRole.BackendEngineer or AgentRole.FrontendEngineer or AgentRole.InfrastructureEngineer).ToArray();
        var requested = result.ReworkTargets ?? [];
        if (requested.Any(x => !Enum.TryParse<AgentRole>(x, out var role) || !developers.Contains(role)))
            throw new InvalidOperationException("Rework target is not an authorized selected developer.");
        var targets = requested.Length > 0 ? requested.Select(Enum.Parse<AgentRole>).Distinct().ToArray() : developers.Length == 1 ? developers : [];
        if (targets.Length == 0) throw new TaskReconciliationException(ExecutionOutcome.AwaitingHumanInput, "Rework responsibility is ambiguous. Identify the responsible selected implementation specialists from the findings.");
        var stage = run.CurrentStage;
        var artifactId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{run.Id}/{stage}/{run.Iteration}/artifact/{source}"))).ToLowerInvariant();
        var findingIds = (result.Findings ?? []).Select((_, i) => $"{artifactId}#/Findings/{i}")
            .Concat((result.Checks ?? []).Select((check, i) => (check, i)).Where(x => !x.check.Passed).Select(x => $"{artifactId}#/Checks/{x.i}")).ToArray();
        return developers.Where(targets.Contains).Select(target => new ReworkTransition(source, target, result.Summary, findingIds, $"{run.Id}/task/{run.TaskIndex}", run.Iteration + 1, revision, DateTimeOffset.UtcNow)).ToArray();
    }
}
