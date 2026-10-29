using AIEngineeringPlatform.Application;
namespace AIEngineeringPlatform.LocalExecutor;

// Decisions use repository files and administrator-owned argument arrays, never model shell text.
public static class ProfileSelection
{
    public static object Capabilities(string[] files, IReadOnlyDictionary<string, ExecutionProfile> profiles) => new
    {
        BootstrapOperations = new[] { "WriteFile", "ApplyPatch" },
        NetworkEnabled = false,
        DependencyAvailability = "Unverified: configured images and commands do not prove SDK, package cache or browser availability. Verify through approved isolated profiles.",
        Profiles = profiles.OrderBy(x => x.Key).Select(x => new
        {
            Name = x.Key, x.Value.Command, x.Value.RequiredFiles,
            Applicable = Applicable(files, x.Value),
            Purpose = x.Key is "all" or "backend" or "frontend" or "e2e" or "node" ? "Test" : "Command",
            Image = x.Value.Image
        }).ToArray()
    };

    public static string[] Available(string[] files, IReadOnlyDictionary<string, ExecutionProfile> profiles)
        => profiles.Where(x => Applicable(files, x.Value)).Select(x => x.Key).Where(x => x is "all" or "backend" or "frontend" or "e2e" or "node").Order().ToArray();

    public static bool Applicable(string[] files, ExecutionProfile profile)
    {
        var command = profile.Command;
        if (command.Length == 0) return false;
        if (command[0] == "node") return command.Length == 2 && command[1] == "--test" && files.Any(x => x.EndsWith(".test.mjs", StringComparison.Ordinal) || x.EndsWith(".test.js", StringComparison.Ordinal) || x.StartsWith("test/", StringComparison.Ordinal));
        if (command[0] == "dotnet") return files.Any(x => x.EndsWith(".csproj", StringComparison.Ordinal) || x.EndsWith(".sln", StringComparison.Ordinal) || x.EndsWith(".slnx", StringComparison.Ordinal));
        if (command[0] == "npm")
        {
            var prefix = Array.IndexOf(command, "--prefix");
            var directory = prefix >= 0 && prefix + 1 < command.Length ? command[prefix + 1].TrimEnd('/') + "/" : "";
            return files.Contains(directory + "package.json") && (!command.Contains("ci") || files.Contains(directory + "package-lock.json") || files.Contains(directory + "npm-shrinkwrap.json"));
        }
        // Composite administrator profiles must declare their prerequisites explicitly.
        return profile.RequiredFiles.Length > 0 && profile.RequiredFiles.All(files.Contains);
    }
    public static void Require(string[] files, ExecutionProfile profile)
    {
        if (!Applicable(files, profile)) throw new ConfigurationException("Execution profile is incompatible with repository files or required manifest/lockfile is missing.");
    }
}
