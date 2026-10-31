using AIEngineeringPlatform.LocalExecutor;
using Xunit;
using Microsoft.Extensions.Configuration;
namespace AIEngineeringPlatform.LocalExecutor.Tests;
public sealed class ManagedConfigurationTests
{
    private static readonly string Image = "sha256:" + new string('a', 64);
    [Fact] public void OwnerAllowlistProducesOnlyFixedPinnedProfiles()
    {
        var repository = ManagedExecutorConfiguration.ParseRepository("owner/aep-sandbox-demo@123:main", Image);
        Assert.Equal(123, repository.InstallationId);
        Assert.All(repository.Profiles.Values, profile => { Assert.Equal(Image, profile.Image); Assert.Equal(["python3", "/opt/aep/profiles.py"], profile.Command[..2]); });
        Assert.Contains("backend", ProfileSelection.Available(["backend/tests/Demo.Tests/Demo.Tests.csproj"], repository.Profiles));
        Assert.DoesNotContain("backend", ProfileSelection.Available(["README.md"], repository.Profiles));
    }
    [Theory] [InlineData("owner/repo@0:main")] [InlineData("owner/../repo@1:main")] [InlineData("owner/repo@1:../main")] [InlineData("owner/repo@1:main;echo evil")]
    public void MalformedAllowlistFailsClosed(string input) => Assert.Throws<ArgumentException>(() => ManagedExecutorConfiguration.ParseRepository(input, Image));
    [DockerFact] public async Task ApprovedDigestRemainsAvailableAfterTheMovingAliasIsRemoved()
    {
        var image = Environment.GetEnvironmentVariable("EXECUTOR_TEST_IMAGE") ?? throw new InvalidOperationException("Pinned test image required.");
        var directory = Path.Combine(Path.GetTempPath(), "aep-managed-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var alias = "aep-managed-runtime:test-" + Guid.NewGuid().ToString("N");
        var process = new ControlledProcess(new() { StateDirectory = directory });
        try
        {
            Assert.Equal(0, (await process.RunAsync("/usr/bin/docker", ["image", "tag", image, alias], null, 15, 1000, default)).ExitCode);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["EXECUTOR_STATE_DIRECTORY"] = directory, ["EXECUTOR_RUNTIME_IMAGE"] = alias }).Build();
            await ManagedExecutorConfiguration.CreateAsync(configuration, default);
            Assert.Equal(0, (await process.RunAsync("/usr/bin/docker", ["image", "rm", alias], null, 15, 1000, default)).ExitCode);
            var retained = await process.RunAsync("/usr/bin/docker", ["image", "inspect", "--format", "{{.Id}}", "aep-managed-runtime:pin-" + image[7..]], null, 15, 1000, default);
            Assert.Equal(0, retained.ExitCode);
            Assert.Equal(image, retained.Output.Trim());
        }
        finally { await process.RunAsync("/usr/bin/docker", ["image", "rm", alias], null, 15, 1000, default); Directory.Delete(directory, true); }
    }

    [Fact] public void IndependentContainerStateVolumesCannotShareCleanupOwnership()
    {
        var first = new ExecutorOptions { StateDirectory = "/var/lib/aep-executor", WorkloadOwnershipId = new string('a', 32) };
        var second = new ExecutorOptions { StateDirectory = "/var/lib/aep-executor", WorkloadOwnershipId = new string('b', 32) };
        var one = new DockerWorkload(first, new(first));
        var two = new DockerWorkload(second, new(second));
        Assert.NotEqual(one.OwnershipLabel, two.OwnershipLabel);
        Assert.Equal(one.OwnershipLabel, new DockerWorkload(first, new(first)).OwnershipLabel);
    }

}
