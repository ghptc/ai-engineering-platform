using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using AIEngineeringPlatform.LocalExecutor;
using System.Text.Json;
using Xunit;
namespace AIEngineeringPlatform.LocalExecutor.Tests;

public sealed class SandboxRepositoryFactAttribute : FactAttribute
{
    public SandboxRepositoryFactAttribute() { if (Environment.GetEnvironmentVariable("VERIFY_DISPOSABLE_REPOSITORY") != "1") Skip = "Opt-in read-only remote Git/PR verification and isolated Node tests in the configured disposable sandbox."; }
}
public class DisposableRepositoryTests
{
    [SandboxRepositoryFact]
    public async Task ExistingDisposableDefaultRevisionPassesNodeTestsWithoutNewDelivery()
    {
        var config = Environment.GetEnvironmentVariable("EXECUTOR_CONFIG_PATH") ?? throw new InvalidOperationException("Set EXECUTOR_CONFIG_PATH.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(config));
        var options = document.RootElement.GetProperty("Executor").Deserialize<ExecutorOptions>()!;
        var repository = Assert.Single(options.Repositories);
        Assert.StartsWith("aep-sandbox-", repository.Name);
        options.StateDirectory = Path.Combine(Path.GetTempPath(), "aep-reconcile-" + Guid.NewGuid().ToString("N"));
        var image = Environment.GetEnvironmentVariable("EXECUTOR_TEST_IMAGE") ?? throw new InvalidOperationException("Set immutable EXECUTOR_TEST_IMAGE.");
        repository.Profiles = new() { ["node"] = new() { Image = image, Command = ["node", "--test"] } };
        options.Validate();
        var connection = new RepositoryConnection(repository.Owner, repository.Name, repository.InstallationId, repository.DefaultBranch);
        var process = new ControlledProcess(options);
        var executor = new LocalExecutionService(options, process, new DockerWorkload(options, process));
        using var http = new HttpClient();
        var github = new GitHubSourceControl(http, options.GitHubAppId, options.GitHubPrivateKeyPath, null);
        async Task<SandboxResponse> Execute(string id, string operation, object args) => await executor.ExecuteAsync(new(id, "reconcile", connection, operation, JsonSerializer.SerializeToElement(args), Fence: 1, LeaseOwner: "verification"), default);
        try
        {
            var metadataBefore = await github.ExecuteAsync(connection, "ReadRepository", JsonSerializer.SerializeToElement(new { }), "metadata", default);
            var source = await Execute("source", "ReadFile", new { path = "feature.mjs" });
            var test = await Execute("test", "ReadFile", new { path = "feature.test.mjs" });
            Assert.Contains("return 2", source.Output); Assert.Matches(@"answer\(\),\s*2", test.Output);
            var diff = await Execute("diff", "InspectDiff", new { }); Assert.Equal("", diff.Output);
            var verified = await Execute("tests", "RunTests", new { profile = "node" }); Assert.True(verified.Success, verified.Output); Assert.Equal(diff.DiffHash, verified.DiffHash);
            var facts = await Execute("facts", "ReadRepository", new { }); Assert.Equal(verified.DiffHash, facts.DiffHash);
            var prs = await github.ExecuteAsync(connection, "ReconcileRepository", JsonSerializer.SerializeToElement(new { }), "prs", default);
            using var inventory = JsonDocument.Parse(prs);
            Assert.Contains(inventory.RootElement.GetProperty("pullRequests").EnumerateArray(), x => x.GetProperty("merged").ValueKind != JsonValueKind.Null);
            var metadataAfter = await github.ExecuteAsync(connection, "ReadRepository", JsonSerializer.SerializeToElement(new { }), "metadata-after", default);
            using var before = JsonDocument.Parse(metadataBefore); using var after = JsonDocument.Parse(metadataAfter);
            Assert.Equal(before.RootElement.GetProperty("pushed_at").GetString(), after.RootElement.GetProperty("pushed_at").GetString());
            Console.WriteLine($"Disposable verification: {repository.Owner}/{repository.Name}; {facts.Output}; revision={verified.DiffHash}; evidence={verified.EvidenceId}; {verified.Output}; PRs={prs}");
        }
        finally { if (Directory.Exists(options.StateDirectory)) Directory.Delete(options.StateDirectory, true); }
    }
}
