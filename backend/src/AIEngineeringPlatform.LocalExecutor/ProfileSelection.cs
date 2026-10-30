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
            Applicable = x.Key is "all" or "backend" or "frontend" or "e2e" or "node" ? Applicable(files, x.Value) : ApplicableCommand(files, x.Value),
            Purpose = x.Key is "all" or "backend" or "frontend" or "e2e" or "node" ? "Test" : "Command",
            Image = x.Value.Image
        }).ToArray()
    };

    public static string[] Available(string[] files, IReadOnlyDictionary<string, ExecutionProfile> profiles)
        => profiles.Where(x => Applicable(files, x.Value)).Select(x => x.Key).Where(x => x is "all" or "backend" or "frontend" or "e2e" or "node").Order().ToArray();

    public static bool Applicable(string[] files, ExecutionProfile profile)
    {
        var command = profile.Command;
        if (command.Length == 0 || !profile.RequiredFiles.All(files.Contains)) return false;
        if (command[0] == "node") return command.Length == 2 && command[1] == "--test" && files.Any(x => x.EndsWith(".test.mjs", StringComparison.Ordinal) || x.EndsWith(".test.js", StringComparison.Ordinal) || x.StartsWith("test/", StringComparison.Ordinal));
        if (command[0] == "dotnet") return DotNetTargetExists(files, command);
        if (command[0] == "npm")
        {
            var prefix = Array.IndexOf(command, "--prefix");
            var directory = prefix >= 0 && prefix + 1 < command.Length ? command[prefix + 1].TrimEnd('/') + "/" : "";
            return files.Contains(directory + "package.json") && (!command.Contains("ci") || files.Contains(directory + "package-lock.json") || files.Contains(directory + "npm-shrinkwrap.json"));
        }
        // Composite administrator profiles must declare their prerequisites explicitly.
        return profile.RequiredFiles.Length > 0 && profile.RequiredFiles.All(files.Contains);
    }
    public static bool ApplicableCommand(string[] files, ExecutionProfile profile)
        => profile.Command.Length > 0 && profile.RequiredFiles.All(files.Contains)
            && (profile.Command[0] is "dotnet" or "npm" ? Applicable(files, profile) : true);

    private static bool DotNetTargetExists(string[] files, string[] command)
    {
        static bool Manifest(string path) => path.EndsWith(".csproj", StringComparison.Ordinal) || path.EndsWith(".sln", StringComparison.Ordinal) || path.EndsWith(".slnx", StringComparison.Ordinal);
        if (command.Length > 1 && command[1] is "--info" or "--version" or "--list-sdks" or "--list-runtimes") return true;
        var target = ".";
        for (var i = 2; i < command.Length; i++)
        {
            if (command[i] == "--") break;
            if (command[i] == "--project") { if (++i >= command.Length) return false; target = command[i]; break; }
            if (command[i] is "-c" or "--configuration" or "-f" or "--framework" or "-r" or "--runtime" or "-o" or "--output" or "-v" or "--verbosity" or "--arch" or "--os") { i++; continue; }
            if (command[i].StartsWith('-')) continue;
            target = command[i]; break;
        }
        if (target.StartsWith("./", StringComparison.Ordinal)) target = target[2..];
        if (target != "." && (Path.IsPathRooted(target) || target.Contains('\\') || target.Split('/').Any(x => x is ".." or ".git"))) return false;
        if (Manifest(target)) return files.Contains(target);
        var prefix = target == "." ? "" : target.TrimEnd('/') + "/";
        var candidates = files.Where(x => x.StartsWith(prefix, StringComparison.Ordinal) && !x[prefix.Length..].Contains('/') && Manifest(x)).ToArray();
        var solutions = candidates.Count(x => x.EndsWith(".sln", StringComparison.Ordinal) || x.EndsWith(".slnx", StringComparison.Ordinal));
        return solutions == 1 || solutions == 0 && candidates.Length == 1;
    }
    public static void Require(string[] files, ExecutionProfile profile)
    {
        if (!Applicable(files, profile)) throw new ConfigurationException("Execution profile is incompatible with repository files or required manifest/lockfile is missing.");
    }
}
