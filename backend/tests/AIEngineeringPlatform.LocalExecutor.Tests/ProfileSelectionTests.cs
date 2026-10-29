using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.LocalExecutor;
using Xunit;
namespace AIEngineeringPlatform.LocalExecutor.Tests;

public class ProfileSelectionTests
{
    [Fact]
    public void NodeOnlyRepositoryAdvertisesOnlyFixedNodeTests()
    {
        var profiles = new Dictionary<string, ExecutionProfile> {
            ["backend"] = new() { Command = ["dotnet", "test", "backend"] },
            ["frontend"] = new() { Command = ["npm", "ci", "--prefix", "frontend"] },
            ["all"] = new() { Command = ["python3", "-c", "composite administrator command"] },
            ["node"] = new() { Command = ["node", "--test"] }
        };
        Assert.Equal(["node"], ProfileSelection.Available(["feature.mjs", "feature.test.mjs"], profiles));
        Assert.Throws<ConfigurationException>(() => ProfileSelection.Require(["feature.test.mjs"], profiles["backend"]));
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NpmCiRequiresBothManifestAndLockfile(bool manifest, bool locked)
    {
        var files = new List<string>();
        if (manifest) files.Add("frontend/package.json");
        if (locked) files.Add("frontend/package-lock.json");
        Assert.Equal(manifest && locked, ProfileSelection.Applicable(files.ToArray(), new() { Command = ["npm", "ci", "--offline", "--prefix", "frontend"] }));
    }
    [Fact]
    public void NodeTestProfileCannotSubstituteShellOrArbitraryNodeArguments()
        => Assert.False(ProfileSelection.Applicable(["feature.test.mjs"], new() { Command = ["node", "-e", "arbitrary code"] }));
}
