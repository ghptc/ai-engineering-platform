using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace AIEngineeringPlatform.LocalExecutor;

// Owner deployment input only. Model requests cannot select tags, images, profiles or repositories.
public static class ManagedExecutorConfiguration
{
    public static async Task<ExecutorOptions> CreateAsync(IConfiguration configuration, CancellationToken ct)
    {
        var options = new ExecutorOptions
        {
            StateDirectory = configuration["EXECUTOR_STATE_DIRECTORY"] ?? "/var/lib/aep-executor",
            SocketPath = configuration["EXECUTOR_SOCKET_PATH"] ?? "/run/aep-executor/executor.sock",
            TokenFile = configuration["EXECUTOR_TOKEN_FILE"] ?? "/run/aep-executor/control-token",
            GitHubAppId = configuration["GITHUB_APP_ID"],
            GitHubPrivateKeyPath = configuration["GITHUB_APP_PRIVATE_KEY_PATH"]
        };
        Directory.CreateDirectory(options.StateDirectory);
        if (new DirectoryInfo(options.StateDirectory).LinkTarget != null) throw new InvalidOperationException("Executor state directory cannot be linked.");
        var identityPath = Path.Combine(options.StateDirectory, "instance-id");
        if (!File.Exists(identityPath))
        {
            using var identity = new FileStream(identityPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var bytes = System.Text.Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N"));
            identity.Write(bytes); identity.Flush(true);
        }
        options.WorkloadOwnershipId = (await File.ReadAllTextAsync(identityPath, ct)).Trim();
        var tag = configuration["EXECUTOR_RUNTIME_IMAGE"] ?? "aep-managed-runtime:local";
        if (!Regex.IsMatch(tag, @"\Aaep-managed-runtime:[a-zA-Z0-9_.-]{1,80}\z")) throw new ArgumentException("Only an owner-built aep-managed-runtime tag may be resolved.");
        var process = new ControlledProcess(options);
        var image = await process.RunAsync("/usr/bin/docker", ["image", "inspect", "--format", "{{.Id}}", tag], null, 15, 1000, ct);
        var id = image.Output.Trim();
        if (image.ExitCode != 0 || !Regex.IsMatch(id, @"\Asha256:[a-f0-9]{64}\z")) throw new InvalidOperationException("Runtime image missing. Rebuild the runtime-image Compose service before starting the executor.");
        // Retain the approved digest under its own owner-only tag. Rebuilding the moving Compose
        // tag must not remove a digest still used by a running daemon or persisted receipts.
        var retained = await process.RunAsync("/usr/bin/docker", ["image", "tag", id, "aep-managed-runtime:pin-" + id[7..]], null, 15, 1000, ct);
        if (retained.ExitCode != 0) throw new InvalidOperationException("Cannot retain the approved runtime digest; refusing to start.");
        var entries = (configuration["EXECUTOR_REPOSITORIES"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        options.Repositories = entries.Select(value => ParseRepository(value, id)).ToArray();
        if (options.Repositories.Select(x => x.Owner + "/" + x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Repositories.Length) throw new ArgumentException("Duplicate executor repository allowlist entry.");
        options.Validate();
        return options;
    }
    public static AuthorizedRepository ParseRepository(string value, string image)
    {
        var match = Regex.Match(value, @"\A(?<owner>[A-Za-z0-9-]{1,39})/(?<name>[A-Za-z0-9_.-]{1,100})@(?<installation>[1-9][0-9]{0,18}):(?<branch>[A-Za-z0-9/_-]{1,100})\z");
        if (!match.Success || !long.TryParse(match.Groups["installation"].Value, out var installation)) throw new ArgumentException("Use owner/repository@installation-id:default-branch in EXECUTOR_REPOSITORIES.");
        ExecutionProfile Profile(string[] command, string[] files) => new() { Image = image, Command = command, RequiredFiles = files, MemoryMb = 1024, Cpus = 1, TimeoutSeconds = 120 };
        var repository = new AuthorizedRepository
        {
            Owner = match.Groups["owner"].Value, Name = match.Groups["name"].Value, InstallationId = installation, DefaultBranch = match.Groups["branch"].Value,
            Profiles = new(StringComparer.Ordinal)
            {
                ["build"] = Profile(["python3", "/opt/aep/profiles.py", "build"], []),
                ["typecheck"] = Profile(["python3", "/opt/aep/profiles.py", "typecheck"], ["frontend/package.json", "frontend/package-lock.json"]),
                ["backend"] = Profile(["python3", "/opt/aep/profiles.py", "backend"], ["backend"]),
                ["frontend"] = Profile(["python3", "/opt/aep/profiles.py", "frontend"], ["frontend/package.json", "frontend/package-lock.json"])
            }
        };
        // Backend target discovery is bounded by the trusted profile; directories are not source files.
        repository.Profiles["backend"].RequiredFiles = [];
        var validation = new ExecutorOptions { Repositories = [repository] }; validation.Validate();
        return repository;
    }
}
