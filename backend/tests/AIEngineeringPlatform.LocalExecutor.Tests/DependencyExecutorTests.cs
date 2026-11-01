using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Application.Dependencies;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.LocalExecutor;
using System.Text;
using System.Text.Json;
using Xunit;
namespace AIEngineeringPlatform.LocalExecutor.Tests;
public sealed class RegistryFactAttribute : FactAttribute
{
    public RegistryFactAttribute() { if (Environment.GetEnvironmentVariable("RUN_DEPENDENCY_REGISTRY_TESTS") != "1" || Environment.GetEnvironmentVariable("RUN_LOCAL_EXECUTOR_TESTS") != "1") Skip = "Explicitly opt in to real bounded public npm acquisition plus Docker with RUN_DEPENDENCY_REGISTRY_TESTS=1 RUN_LOCAL_EXECUTOR_TESTS=1 EXECUTOR_TEST_IMAGE=<immutable helper image>. No model calls or remote Git writes."; }
}
public sealed partial class ExecutorTests
{
    [Fact]
    public void SourceCannotInjectTrustedDependencyCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "aep-cache-injection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".aep-dependency-cache"));
        try
        {
            File.WriteAllText(Path.Combine(root, ".aep-dependency-cache", "forged"), "untrusted");
            Assert.Throws<ArgumentException>(() => WorkspaceFiles.Archive(root, 1));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void LegacyCatalogCannotAuthorizeForgedTransitiveLockEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "aep-lock-injection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "frontend"));
        try
        {
            File.WriteAllText(Path.Combine(root, "frontend/package.json"), JsonSerializer.Serialize(new { name = "fixture", version = "1.0.0", dependencies = ApprovedFrontendBootstrap.Dependencies }));
            ApprovedFrontendBootstrap.Prepare(root);
            var content = File.ReadAllText(Path.Combine(root, "frontend/package-lock.json"));
            Assert.True(ApprovedFrontendBootstrap.IsCatalogLock(content));
            var forged = System.Text.Json.Nodes.JsonNode.Parse(content)!;
            forged["packages"]!["node_modules/forged"] = System.Text.Json.Nodes.JsonNode.Parse("{\"version\":\"1.0.0\",\"resolved\":\"https://evil.invalid/pkg.tgz\"}");
            Assert.False(ApprovedFrontendBootstrap.IsCatalogLock(forged.ToJsonString()));
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("//evil.invalid/package")]
    [InlineData("/package?token=secret")]
    [InlineData("/../escape")]
    [InlineData("/package/%2e%2e/escape")]
    [InlineData("/https:%2f%2fevil.invalid")]
    [InlineData("/package/-/package.tgz#fragment")]
    public void RegistryBrokerRejectsUntrustedOriginsAndPaths(string path) => Assert.Throws<UnauthorizedAccessException>(() => DependencyExecutor.RegistryUri(path));
    [Theory]
    [InlineData("/is-number")]
    [InlineData("/@scope%2fpackage")]
    [InlineData("/is-number/-/is-number-7.0.0.tgz")]
    public void RegistryBrokerPinsHttpsOrigin(string path)
    {
        var uri = DependencyExecutor.RegistryUri(path); Assert.Equal("registry.npmjs.org", uri.Host); Assert.Equal("https", uri.Scheme); Assert.Equal("", uri.UserInfo);
    }
    [Fact]
    public void ArbitraryManifestSourcesAreRejected()
    {
        var manifest = System.Text.Json.Nodes.JsonNode.Parse("{\"dependencies\":{\"is-number\":\"https://evil.invalid/pkg.tgz\"}}")!.AsObject();
        Assert.Throws<ArgumentException>(() => DependencyExecutor.ValidateManifest(manifest));
        Assert.Throws<ArgumentException>(() => DependencyExecutor.ValidateManifest(System.Text.Json.Nodes.JsonNode.Parse("{\"workspaces\":[\"*\"]}")!.AsObject()));
    }
    [Fact]
    public async Task CachedButUnapprovedDirectDependencyCannotStartAnOfflineProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "aep-dependency-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "workspace", "frontend"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "workspace", "frontend/package.json"), "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"dependencies\":{\"is-number\":\"7.0.0\"}}");
            var request = Request("unapproved-run", "unapproved-build", "RunCommand", new { profile = "build" }) with { ProjectId = "other-project" };
            var error = await Assert.ThrowsAsync<ExecutorFailureException>(() => DependencyPreparation.CacheForProfileAsync(new() { StateDirectory = root }, request, Path.Combine(root, "workspace"), default));
            Assert.Equal(ExecutorFailureReason.DependencyApprovalRequired, error.Reason); Assert.False(error.OutcomeUnknown); Assert.Equal(422, error.StatusCode);
        }
        finally { Directory.Delete(root, true); }
    }
    [RegistryFact]
    public async Task OwnerScopedResolutionAcquiresRealPackageAndReplaysSourcePreparationOffline()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string run = "dependency-source"; const string project = "dependency-project";
        var repository = new RepositoryConnection("test-owner", "aep-fixture", 1);
        await fixture.Executor.ExecuteAsync(Request(run, "read-dependency", "ReadRepository", new { }), default);
        await fixture.Executor.ExecuteAsync(Request(run, "manifest-dependency", "WriteFile", new { path = "frontend/package.json", content = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true,\"dependencies\":{}}" }), default);
        var proposal = new DependencyProposal(1, "npm", "npm", "https://registry.npmjs.org", [new("is-number", "7.0.0", false, "Generic registry acquisition certification")], [], "License and vulnerability status must be reviewed; no scripts permitted", [], [], ["Fixture verification"], []);
        var id = new string('b', 64);
        var request = new DependencyApprovalRequest(id, project, run, RunStage.Planning, AgentRole.Architect, "fixture-task", "fixture-op", 0, proposal, DependencyApprovalStatus.Resolving, 1, DateTimeOffset.UtcNow, [new("Resolve", "test-owner", DateTimeOffset.UtcNow, [], true, "Explicit opt-in public registry fixture")], ExecutionDeadline: DateTimeOffset.UtcNow.AddMinutes(2));
        var dependencies = new DependencyExecutor(fixture.Options, new(fixture.Options, fixture.Process), fixture.Process);
        var resolution = await dependencies.ResolveAsync(request, repository, default);
        Assert.Contains(resolution.Packages, p => p.Name == "is-number" && p.Version == "7.0.0" && p.Direct && p.Integrity.StartsWith("sha512-", StringComparison.Ordinal));
        Assert.Equal(resolution.PlanSha256, (await dependencies.ReadAsync(id, default))!.PlanSha256);
        var packageIds = resolution.Packages.Select(p => p.Name + "@" + p.Version).Distinct().ToArray();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Executor.ExecuteAsync(Request(run, "unapproved-prepare", "PrepareApprovedDependencies", new { requestId = id }) with { ProjectId = project }, default));
        await dependencies.GrantAsync(new(id, project, run, repository, resolution.PlanSha256, packageIds, "owner", DateTimeOffset.UtcNow), default);
        var prepare = Request(run, "prepare-dependencies", "PrepareApprovedDependencies", new { requestId = id }) with { ProjectId = project };
        var beforeWrong = prepare with { OperationId = "wrong-project-dependencies", ProjectId = "other-project" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Executor.ExecuteAsync(beforeWrong, default));
        var prepared = await fixture.Executor.ExecuteAsync(prepare, default); Assert.True(prepared.Success);
        var replay = await fixture.Executor.ExecuteAsync(prepare with { Fence = 2 }, default); Assert.Equal(prepared.EvidenceId, replay.EvidenceId); Assert.Equal(prepared.DiffHash, replay.DiffHash);
        var workspace = Path.Combine(fixture.Root, "runs", run, "repository");
        Assert.Equal(resolution.Lock, await File.ReadAllTextAsync(Path.Combine(workspace, "frontend/package-lock.json")));
        fixture.Options.Repositories[0].Profiles["format"] = new() { Image = Environment.GetEnvironmentVariable("EXECUTOR_TEST_IMAGE")!, Command = ["python3", "-c", "import os,subprocess; assert os.environ.get('OPENAI_API_KEY') is None; subprocess.run(['npm','ci','--offline','--ignore-scripts','--prefix','frontend','--no-audit','--no-fund'],check=True); subprocess.run(['node','-e',\"if(!require('./frontend/node_modules/is-number')(42))process.exit(1); console.log('actual offline approved dependency works')\"],check=True)"] };
        var verified = await fixture.Executor.ExecuteAsync(Request(run, "offline-approved", "RunCommand", new { profile = "format" }) with { ProjectId = project, Fence = 3 }, default);
        Assert.True(verified.Success, verified.Output); Assert.Contains("actual offline approved dependency works", verified.Output);
        await File.AppendAllTextAsync(Path.Combine(workspace, "frontend/package-lock.json"), " ");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Executor.ExecuteAsync(Request(run, "tampered-lock", "RunCommand", new { profile = "format" }) with { ProjectId = project, Fence = 4 }, default));
        Assert.Equal(fixture.OriginalHead, (await fixture.Process.RunAsync("/usr/bin/git", ["--git-dir", fixture.Remote, "rev-parse", "HEAD"], null, 10, 10000, default)).Output.Trim());
        if (Environment.GetEnvironmentVariable("AEP_DEPENDENCY_EVIDENCE_DIRECTORY") is { Length: > 0 } reportDirectory)
        {
            Directory.CreateDirectory(reportDirectory);
            var report = new { Fixture = "Disposable local bare Git; no remote repository/model mutation", RunId = run, ProjectId = project, RequestId = id, resolution.PlanSha256, resolution.CacheSha256, resolution.ResolverImage, resolution.Packages, PreparationEvidenceId = prepared.EvidenceId, ReplayEvidenceId = replay.EvidenceId, SourceRevisionAfterPreparation = prepared.DiffHash, OfflineUseEvidenceId = verified.EvidenceId, ActualExitCode = verified.ExitCode, verified.Execution, DefaultHead = fixture.OriginalHead, DefaultHeadUnchanged = true, CrossProjectDenied = true, UnapprovedPreparationDenied = true, TamperedLockDenied = true };
            await File.WriteAllTextAsync(Path.Combine(reportDirectory, "live-acquisition-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
