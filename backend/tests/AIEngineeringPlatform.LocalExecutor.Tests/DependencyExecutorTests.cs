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
public sealed class LegacyRuntimeFactAttribute : FactAttribute
{
    public LegacyRuntimeFactAttribute() { if (Environment.GetEnvironmentVariable("RUN_LOCAL_EXECUTOR_TESTS") != "1" || Environment.GetEnvironmentVariable("EXECUTOR_LEGACY_TEST_IMAGE") == null) Skip = "Opt in with an immutable EXECUTOR_LEGACY_TEST_IMAGE lacking dependency contract 2."; }
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
    [Theory]
    [InlineData("{\"optional\":true,\"os\":[\"darwin\"]}", false)]
    [InlineData("{\"optional\":true,\"cpu\":[\"arm64\"]}", false)]
    [InlineData("{\"optional\":true,\"libc\":[\"musl\"]}", false)]
    [InlineData("{\"optional\":true,\"os\":[\"!linux\"]}", false)]
    [InlineData("{\"optional\":true,\"os\":[\"linux\"],\"cpu\":[\"x64\"],\"libc\":[\"glibc\"]}", true)]
    [InlineData("{\"os\":[\"darwin\"]}", true)]
    public void OnlyIncompatibleOptionalTarballsCanBeOmitted(string json, bool required) =>
        Assert.Equal(required, DependencyExecutor.RequiresRuntimeCache(System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject(), "x64", "glibc"));

    [DockerFact]
    public async Task UnknownAcquisitionMarkerCannotBeArchivedOrReplayed()
    {
        await using var fixture = await Fixture.CreateAsync(readmeOnly: true);
        const string run = "uncertain-dependency"; var id = DependencyGovernance.Hash(fixture.Root + "/uncertain");
        var directory = Path.Combine(fixture.Root, "dependencies", id); Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, "acquisition-started.json"); await File.WriteAllTextAsync(marker, "preserved uncertain acquisition");
        var originalImage = fixture.Options.Repositories[0].Profiles.Values.First().Image;
        foreach (var profile in fixture.Options.Repositories[0].Profiles.Values) profile.Image = "sha256:" + new string('0', 64);
        var proposal = new DependencyProposal(1, "npm", "npm", "https://registry.npmjs.org", [new("is-number", "7.0.0", false, "Fixture")], [], "Unknown", [], [], [], []);
        var request = new DependencyApprovalRequest(id, "project", run, RunStage.Planning, AgentRole.Architect, "task", "op", 0, proposal, DependencyApprovalStatus.Resolving, 3, DateTimeOffset.UtcNow,
            [new("Resolve", "owner", DateTimeOffset.UtcNow, [], true, "Explicit authorization cannot override an uncertain marker")], ExecutionDeadline: DateTimeOffset.UtcNow.AddMinutes(2));
        var dependencies = new DependencyExecutor(fixture.Options, new(fixture.Options, fixture.Process), fixture.Process);
        var error = await Assert.ThrowsAsync<DependencyAcquisitionException>(() => dependencies.ResolveAsync(request, new("test-owner", "aep-fixture", 1), default));
        Assert.False(error.Failure.OutcomeKnown); Assert.Equal("Reconcile", error.Failure.RecoveryAction);
        Assert.Equal("preserved uncertain acquisition", await File.ReadAllTextAsync(marker)); Assert.Single(Directory.GetFiles(directory));
        foreach (var profile in fixture.Options.Repositories[0].Profiles.Values) profile.Image = originalImage;
        var legacyName = "aep-deps-" + id[..24];
        var legacy = await fixture.Process.RunAsync("/usr/bin/docker", ["create", "--name", legacyName, "--network", "none", "--read-only", "--user", "65532:65532", "--cap-drop", "ALL", "--security-opt", "no-new-privileges=true", "--entrypoint", "/usr/bin/true", originalImage], null, 15, 10000, default);
        Assert.Equal(0, legacy.ExitCode);
        try
        {
            var active = await Assert.ThrowsAsync<DependencyAcquisitionException>(() => dependencies.ReconcileAsync(id, default));
            Assert.False(active.Failure.OutcomeKnown); Assert.Equal("Reconcile", active.Failure.RecoveryAction);
            Assert.False(File.Exists(Path.Combine(directory, "failure.json")));
        }
        finally { await fixture.Process.RunAsync("/usr/bin/docker", ["rm", legacy.Output.Trim()], null, 15, 10000, default); }
        var uncertainReceipt = JsonSerializer.Serialize(new DependencyFailure("cleanup", "dependency_cleanup_unverified", true, false, "Reconcile"));
        await File.WriteAllTextAsync(Path.Combine(directory, "failure.json"), uncertainReceipt);
        var reconciled = await Assert.ThrowsAsync<DependencyAcquisitionException>(() => dependencies.ReconcileAsync(id, default));
        Assert.True(reconciled.Failure.OutcomeKnown); Assert.Equal("dependency_acquisition_interrupted", reconciled.Failure.Code);
        Assert.Equal(uncertainReceipt, await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory, "failure.json.prior-reconciliation-*"))));
        Assert.Equal("preserved uncertain acquisition", await File.ReadAllTextAsync(marker));
    }

    [LegacyRuntimeFact]
    public async Task PinnedLegacyHelperFailsBeforeRegistryAcquisitionAndPersistsDiagnosis()
    {
        await using var fixture = await Fixture.CreateAsync(readmeOnly: true);
        const string run = "legacy-runtime";
        await fixture.Executor.ExecuteAsync(Request(run, "read-legacy", "ReadRepository", new { }), default);
        foreach (var profile in fixture.Options.Repositories[0].Profiles.Values) profile.Image = Environment.GetEnvironmentVariable("EXECUTOR_LEGACY_TEST_IMAGE")!;
        var proposal = new DependencyProposal(1, "npm", "npm", "https://registry.npmjs.org", [new("is-number", "7.0.0", false, "Fixture")], [], "Unknown", [], [], [], []);
        var request = new DependencyApprovalRequest(DependencyGovernance.Hash(fixture.Root + "/legacy"), "project", run, RunStage.Planning, AgentRole.Architect, "task", "op", 0, proposal, DependencyApprovalStatus.Resolving, 1, DateTimeOffset.UtcNow,
            [new("Resolve", "owner", DateTimeOffset.UtcNow, [], true, "Explicit fixture authorization")], ExecutionDeadline: DateTimeOffset.UtcNow.AddMinutes(2));
        var dependencies = new DependencyExecutor(fixture.Options, new(fixture.Options, fixture.Process), fixture.Process);
        var error = await Assert.ThrowsAsync<DependencyAcquisitionException>(() => dependencies.ResolveAsync(request, new("test-owner", "aep-fixture", 1), default));
        Assert.Equal("dependency_runtime_incompatible", error.Failure.Code); Assert.False(error.Failure.AcquisitionStarted); Assert.True(error.Failure.OutcomeKnown);
        var persisted = await Assert.ThrowsAsync<DependencyAcquisitionException>(() => dependencies.ReadAsync(request.Id, default)); Assert.Equal(error.Failure, persisted.Failure);
    }

    [RegistryFact]
    public async Task ReadmeOnlyIncidentResolvesExactPdfAndBrowserDependencies()
    {
        await using var fixture = await Fixture.CreateAsync(readmeOnly: true);
        const string run = "incident-disposable";
        await fixture.Executor.ExecuteAsync(Request(run, "read-incident", "ReadRepository", new { }), default);
        var proposal = new DependencyProposal(1, "npm", "npm", "https://registry.npmjs.org",
            [new("pdf-lib", "1.17.1", false, "Incident reproduction"), new("pdfjs-dist", "5.4.624", false, "Incident reproduction"), new("@playwright/test", "1.56.1", true, "Incident reproduction")], [], "Owner review required", [], [], ["Disposable README-only fixture"], []);
        var request = new DependencyApprovalRequest(DependencyGovernance.Hash(fixture.Root + "/incident"), "incident-project", run, RunStage.Planning, AgentRole.Architect, "task", "op", 0, proposal,
            DependencyApprovalStatus.Resolving, 1, DateTimeOffset.UtcNow, [new("Resolve", "owner", DateTimeOffset.UtcNow, [], true, "Explicit incident reproduction")], ExecutionDeadline: DateTimeOffset.UtcNow.AddMinutes(2));
        var dependencies = new DependencyExecutor(fixture.Options, new(fixture.Options, fixture.Process), fixture.Process);
        var resolution = await dependencies.ResolveAsync(request, new("test-owner", "aep-fixture", 1), default);
        Assert.All(proposal.Packages, p => Assert.Contains(resolution.Packages, r => r.Direct && r.Name == p.Name && r.Version == p.Version));
        Assert.Equal(resolution.PlanSha256, (await dependencies.ReadAsync(request.Id, default))!.PlanSha256);
        Assert.Equal("npm-v2", resolution.ResolverContract);
        Assert.Contains(resolution.Packages, p => p.CacheRequired == false);
        Assert.Contains(resolution.Packages, p => p.RuntimeCached == true);
        Assert.InRange(resolution.Acquisition!.DownloadedBytes, 1, 64_000_000);
        var packageIds = resolution.Packages.Select(p => p.Name + "@" + p.Version).Distinct().ToArray();
        await dependencies.GrantAsync(new(request.Id, request.ProjectId, run, resolution.Repository, resolution.PlanSha256, packageIds, "owner", DateTimeOffset.UtcNow), default);
        await fixture.Executor.ExecuteAsync(Request(run, "incident-prepare", "PrepareApprovedDependencies", new { requestId = request.Id }) with { ProjectId = request.ProjectId }, default);
        // Keep the complete approved graph intact. A disposable subset smoke fits the fixed 256 MB tmpfs.
        const string smoke = "import json,pathlib,subprocess; root=pathlib.Path('smoke');root.mkdir(); lock=json.loads(pathlib.Path('frontend/package-lock.json').read_text()); deps={'pdf-lib':'1.17.1','pdfjs-dist':'5.4.624','@playwright/test':'1.56.1'}; manifest={'name':'aep-smoke','version':'1.0.0','dependencies':deps}; lock['name']='aep-smoke';lock['version']='1.0.0';lock['packages']['']=manifest; (root/'package.json').write_text(json.dumps(manifest));(root/'package-lock.json').write_text(json.dumps(lock));subprocess.run(['npm','ci','--offline','--ignore-scripts','--omit=optional','--no-audit','--no-fund','--prefix','smoke'],check=True);subprocess.run(['node','-e',\"const assert=require('node:assert/strict');assert.equal(require('./smoke/node_modules/pdfjs-dist/package.json').version,'5.4.624');assert.equal(require('./smoke/node_modules/@playwright/test/package.json').version,'1.56.1');require('./smoke/node_modules/pdf-lib').PDFDocument.create().then(d=>d.save()).then(b=>assert.ok(b.length>100))\"],check=True)";
        fixture.Options.Repositories[0].Profiles["format"] = new() { Image = resolution.ResolverImage!, Command = ["python3", "-c", smoke] };
        var smokeRequest = Request(run, "incident-offline", "RunCommand", new { profile = "format" }) with { ProjectId = request.ProjectId, Fence = 2, MemoryMb = 1024 };
        fixture.Options.Repositories[0].Profiles["format"].Image = "sha256:" + new string('0', 64);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => DependencyPreparation.CacheForProfileAsync(fixture.Options, smokeRequest, Path.Combine(fixture.Root, "runs", run, "repository"), default));
        fixture.Options.Repositories[0].Profiles["format"].Image = resolution.ResolverImage!;
        var installed = await fixture.Executor.ExecuteAsync(smokeRequest, default);
        Assert.True(installed.Success, $"exit={installed.ExitCode}; {installed.Output}; stderr={installed.Execution?.Stderr}");
        if (Environment.GetEnvironmentVariable("AEP_DEPENDENCY_EVIDENCE_DIRECTORY") is { Length: > 0 } evidenceDirectory)
        {
            Directory.CreateDirectory(evidenceDirectory);
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "readme-only-incident.json"), JsonSerializer.Serialize(new {
                Fixture = "Disposable local bare Git repository initialized with README.md only", Packages = proposal.Packages.Select(p => p.Name + "@" + p.Version),
                resolution.ResolverImage, resolution.PlanSha256, resolution.CacheSha256, resolution.Acquisition,
                PackageCount = resolution.Packages.Length, ImageCachedCount = resolution.Packages.Count(p => p.RuntimeCached == true),
                IncompatibleOptionalCount = resolution.Packages.Count(p => p.CacheRequired == false),
                OfflineInstallationExitCode = installed.ExitCode, OfflineSmokeScope = "Three requested packages and their required transitive graph; optional packages omitted", OfflineMemoryMb = 1024, OriginalDefaultHead = fixture.OriginalHead,
                DefaultHeadAfter = (await fixture.Process.RunAsync("/usr/bin/git", ["--git-dir", fixture.Remote, "rev-parse", "HEAD"], null, 10, 10000, default)).Output.Trim()
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Assert.Equal(fixture.OriginalHead, (await fixture.Process.RunAsync("/usr/bin/git", ["--git-dir", fixture.Remote, "rev-parse", "HEAD"], null, 10, 10000, default)).Output.Trim());
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
        var id = DependencyGovernance.Hash(fixture.Root + "/package");
        var request = new DependencyApprovalRequest(id, project, run, RunStage.Planning, AgentRole.Architect, "fixture-task", "fixture-op", 0, proposal, DependencyApprovalStatus.Resolving, 1, DateTimeOffset.UtcNow, [new("Resolve", "test-owner", DateTimeOffset.UtcNow, [], true, "Explicit opt-in public registry fixture")], ExecutionDeadline: DateTimeOffset.UtcNow.AddMinutes(2));
        var dependencies = new DependencyExecutor(fixture.Options, new(fixture.Options, fixture.Process), fixture.Process);
        var source = Path.Combine(fixture.Root, "runs", run, "repository", "frontend", "package.json");
        await File.WriteAllTextAsync(source, "{\"workspaces\":[\"untrusted\"]}");
        var failed = await Assert.ThrowsAsync<DependencyAcquisitionException>(() => dependencies.ResolveAsync(request, repository, default));
        Assert.True(failed.Failure.OutcomeKnown); Assert.False(failed.Failure.AcquisitionStarted);
        Assert.Equal("RepairConfiguration", failed.Failure.RecoveryAction);
        await Assert.ThrowsAsync<DependencyAcquisitionException>(() => dependencies.ReadAsync(id, default));
        await File.WriteAllTextAsync(source, "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true,\"dependencies\":{}}");
        request = request with { Version = 3, Audit = [.. request.Audit, new("Resolve", "test-owner", DateTimeOffset.UtcNow, [], true, "Explicit authorization after correcting fixture manifest")] };
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
