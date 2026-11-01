using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Application.Dependencies;
namespace AIEngineeringPlatform.Api;
public static class DependencyApprovalEndpoints
{
    public static void MapDependencyApprovals(this RouteGroupBuilder api)
    {
        api.MapGet("/dependency-approvals", (string? projectId, string? runId, IDependencyApprovalStore store, CancellationToken ct) => store.ListAsync(projectId, runId, ct));
        api.MapGet("/dependency-approvals/{id}", async (string id, IDependencyApprovalStore store, CancellationToken ct) => await store.GetAsync(id, ct) ?? throw new KeyNotFoundException());
        api.MapPost("/dependency-approvals/{id}/decision", (string id, DependencyDecisionInput input, HttpContext context, DependencyGovernance governance, CancellationToken ct) => governance.DecideAsync(id, input.Version, input.Action, input.PackageIds, input.RegistryAccess, input.Reason, context.User.Identity?.Name ?? throw new UnauthorizedAccessException(), ct));
        api.MapPost("/dependency-approvals/{id}/resume", async (string id, HttpContext context, DependencyGovernance governance, CancellationToken ct) => { await governance.ResumeAsync(id, ct, context.User.Identity?.Name ?? throw new UnauthorizedAccessException()); return Results.Accepted(); });
        api.MapGet("/dependency-contract", () => Results.Ok(new { Version = 1, SupportedEcosystems = new[] { "npm" }, Registry = "https://registry.npmjs.org", InstallScriptsAllowed = false, AgentNetworkAllowed = false, MaxDirectPackages = 20, MaxResolvedPackages = 500, MaxRegistryBytes = 64000000, MaxResolutionSeconds = 120 }));
    }
}
public sealed record DependencyDecisionInput(long Version, string Action, string[] PackageIds, bool RegistryAccess, string Reason);
