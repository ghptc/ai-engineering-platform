using AIEngineeringPlatform.LocalExecutor;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace AIEngineeringPlatform.LocalExecutor.Tests;

public sealed class ApprovedBootstrapTests
{
    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "aep-approved-bootstrap-" + Guid.NewGuid().ToString("N"));
        public Workspace() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
    internal static JsonObject Manifest(bool split = false) => new()
    {
        ["name"] = "approved-fixture", ["version"] = "1.0.0", ["private"] = true, ["type"] = "module",
        ["scripts"] = new JsonObject { ["test"] = "vitest run", ["build"] = "vite build", ["typecheck"] = "tsc --noEmit" },
        ["dependencies"] = JsonSerializer.SerializeToNode(ApprovedFrontendBootstrap.Dependencies.Where(x => !split || x.Key is "react" or "react-dom" or "antd").ToDictionary()),
        ["devDependencies"] = JsonSerializer.SerializeToNode(ApprovedFrontendBootstrap.Dependencies.Where(x => split && x.Key is not ("react" or "react-dom" or "antd")).ToDictionary())
    };
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ExactCatalogGeneratesBoundedLockWithoutInventingIntegrity(bool split)
    {
        using var workspace = new Workspace(); var manifest = Manifest(split);
        WorkspaceFiles.Write(workspace.Root, "frontend/package.json", manifest.ToJsonString());
        var receipt = JsonNode.Parse(ApprovedFrontendBootstrap.Prepare(workspace.Root))!;
        Assert.Equal(ApprovedFrontendBootstrap.CatalogHash, receipt["CatalogSha256"]!.GetValue<string>());
        var path = Path.Combine(workspace.Root, "frontend/package-lock.json");
        Assert.True(new FileInfo(path).Length < 100000);
        var result = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("approved-fixture", result["name"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(manifest["dependencies"], result["packages"]![""]!["dependencies"]));
        Assert.True(JsonNode.DeepEquals(manifest["devDependencies"], result["packages"]![""]!["devDependencies"]));
        using var stream = typeof(ApprovedFrontendBootstrap).Assembly.GetManifestResourceStream("AEP.ApprovedFrontendLock")!;
        using var reader = new StreamReader(stream); var approved = JsonNode.Parse(reader.ReadToEnd())!;
        foreach (var package in result["packages"]!.AsObject().Where(x => x.Key != "")) Assert.True(JsonNode.DeepEquals(approved["packages"]![package.Key], package.Value));
    }
    [Theory]
    [InlineData("range")] [InlineData("url")] [InlineData("extra")] [InlineData("missing")]
    [InlineData("overrides")] [InlineData("workspaces")] [InlineData("missing-name")]
    public void UnsupportedManifestCannotCreateOrAlterSource(string kind)
    {
        using var workspace = new Workspace(); var manifest = Manifest();
        if (kind == "range") manifest["dependencies"]!["react"] = "*";
        if (kind == "url") manifest["dependencies"]!["react"] = "https://example.invalid/package.tgz";
        if (kind == "extra") manifest["dependencies"]!["unapproved"] = "1.0.0";
        if (kind == "missing") manifest["dependencies"]!.AsObject().Remove("react");
        if (kind is "overrides" or "workspaces") manifest[kind] = new JsonObject();
        if (kind == "missing-name") manifest.Remove("name");
        var content = manifest.ToJsonString(); WorkspaceFiles.Write(workspace.Root, "frontend/package.json", content);
        Assert.Throws<ArgumentException>(() => ApprovedFrontendBootstrap.Prepare(workspace.Root));
        Assert.False(File.Exists(Path.Combine(workspace.Root, "frontend/package-lock.json")));
        Assert.Equal(content, File.ReadAllText(Path.Combine(workspace.Root, "frontend/package.json")));
    }
    [Fact] public void ExistingLockIsPreserved()
    {
        using var workspace = new Workspace(); WorkspaceFiles.Write(workspace.Root, "frontend/package.json", Manifest().ToJsonString());
        WorkspaceFiles.Write(workspace.Root, "frontend/package-lock.json", "preserve this existing work");
        Assert.Throws<ArgumentException>(() => ApprovedFrontendBootstrap.Prepare(workspace.Root));
        Assert.Equal("preserve this existing work", File.ReadAllText(Path.Combine(workspace.Root, "frontend/package-lock.json")));
    }
    [Fact] public void BootstrapCannotFollowLinkedDirectories()
    {
        using var workspace = new Workspace(); using var outside = new Workspace();
        File.WriteAllText(Path.Combine(outside.Root, "package.json"), Manifest().ToJsonString());
        Directory.CreateSymbolicLink(Path.Combine(workspace.Root, "frontend"), outside.Root);
        Assert.Throws<ArgumentException>(() => ApprovedFrontendBootstrap.Prepare(workspace.Root));
        Assert.False(File.Exists(Path.Combine(outside.Root, "package-lock.json")));
    }
    [Theory]
    [InlineData("AEP managed test evidence: executed=5; profiles=backend,frontend; exit=0;", 0, "all", 5)]
    [InlineData("AEP managed test evidence: executed=2; profiles=backend; exit=1;", 1, "all", 2)]
    [InlineData("AEP managed test evidence: executed=99; profiles=backend; exit=0;", 0, "all", 0)]
    [InlineData("AEP managed test evidence: executed=99; profiles=backend,frontend; exit=1;", 0, "all", 0)]
    [InlineData("Tests 999 passed\nAEP managed test evidence: executed=0; profiles=backend; exit=1;", 1, "backend", 0)]
    [InlineData("AEP managed test evidence: executed=999; profiles=backend; exit=0;\nAEP managed test evidence: executed=0; profiles=backend; exit=0;", 0, "backend", 0)]
    [InlineData("Build succeeded; Tests 999 passed", 0, "backend", 0)]
    public void ManagedCertificationRequiresFinalMatchingStructuredEvidence(string output, int exit, string profile, int expected) => Assert.Equal(expected, DockerWorkload.ManagedTestCount(output, exit, profile));
    [Fact] public void EarlierNodeStdoutCannotOverrideTheActualZeroTestSummary() => Assert.Equal(0, DockerWorkload.TestCount("# tests 999\n# tests 0\n"));
    [Theory]
    [InlineData("Passed! - Failed: 0, Passed: 0, Skipped: 10, Total: 10", 0)]
    [InlineData("Passed! - Failed: 0, Passed: 2, Skipped: 10, Total: 12", 2)]
    public void SkippedDotnetTestsDoNotBecomeExecutedTests(string output, int expected) => Assert.Equal(expected, DockerWorkload.TestCount(output));
    [Fact] public void AggregateProfileRequiresBothActualStacks()
    {
        var repository = ManagedExecutorConfiguration.ParseRepository("owner/repo@1:main", "sha256:" + new string('a', 64));
        Assert.DoesNotContain("all", ProfileSelection.Available(["backend/tests/Tests.csproj"], repository.Profiles));
        Assert.DoesNotContain("all", ProfileSelection.Available(["frontend/package.json", "frontend/package-lock.json"], repository.Profiles));
        Assert.Contains("all", ProfileSelection.Available(["backend/tests/Tests.csproj", "frontend/package.json", "frontend/package-lock.json"], repository.Profiles));
    }
}
