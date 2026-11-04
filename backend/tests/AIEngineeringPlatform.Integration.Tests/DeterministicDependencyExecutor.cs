using AIEngineeringPlatform.Application.Dependencies;
using AIEngineeringPlatform.Domain;
namespace AIEngineeringPlatform.Integration.Tests;
// Test assembly only. These receipts are never presented as live registry certification.
public sealed class DeterministicDependencyExecutor : IDependencyExecutor
{
    public Task<DependencyResolution> ResolveAsync(DependencyApprovalRequest request, RepositoryConnection repository, CancellationToken ct)
    {
        var plan = new DependencyResolution(request.Id, request.ProjectId, request.RunId, repository, new('a',64), new('b',64), "{}", "{}", new('c',64), request.Proposal.Packages.Select(x => new ResolvedDependency(x.Name, x.Version, "sha512-" + Convert.ToBase64String(new byte[64]), "https://registry.npmjs.org", true, x.Development, "Unknown (deterministic fixture)", "Unknown — not scanned", false)).ToArray(), new('d',64));
        return Task.FromResult(plan with { PlanSha256 = DependencyGovernance.ResolutionFingerprint(plan) });
    }
    public Task<DependencyResolution?> ResolutionAsync(string id, CancellationToken ct) => Task.FromResult<DependencyResolution?>(null);
    public Task<DependencyResolution?> ReconcileAsync(string id, CancellationToken ct) => ResolutionAsync(id, ct);
    public Task GrantAsync(DependencyGrant grant, CancellationToken ct) => Task.CompletedTask;
}
