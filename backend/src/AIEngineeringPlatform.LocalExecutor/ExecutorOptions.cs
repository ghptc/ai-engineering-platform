using System.Text.RegularExpressions;
namespace AIEngineeringPlatform.LocalExecutor;

public sealed class ExecutorOptions
{
    public string StateDirectory { get; set; } = "/var/lib/aep-executor";
    public string SocketPath { get; set; } = "/run/aep-executor/executor.sock";
    public string TokenFile { get; set; } = "/etc/aep-executor/control-token";
    public string DockerHost { get; set; } = "unix:///var/run/docker.sock";
    public string? WorkloadOwnershipId { get; set; }
    public int WorkspaceLimitMb { get; set; } = 64;
    public int RetentionDays { get; set; } = 7;
    public AuthorizedRepository[] Repositories { get; set; } = [];
    public string? GitHubAppId { get; set; }
    public string? GitHubPrivateKeyPath { get; set; }
    public void Validate(bool testing = false)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The local executor requires Linux.");
        if (!Path.IsPathFullyQualified(StateDirectory) || StateDirectory.TrimEnd('/').Split('/').Length < 3 || !Path.IsPathFullyQualified(SocketPath) || !Path.IsPathFullyQualified(TokenFile)) throw new ArgumentException("Use dedicated absolute state, socket and token paths.");
        if (!DockerHost.StartsWith("unix:///", StringComparison.Ordinal) || WorkspaceLimitMb is < 1 or > 256 || RetentionDays is < 1 or > 30) throw new ArgumentException("Docker must use a local Unix socket and bounded storage/retention.");
        if (WorkloadOwnershipId != null && !Regex.IsMatch(WorkloadOwnershipId, @"\A[a-f0-9]{32}\z")) throw new ArgumentException("Invalid executor ownership identity.");
        foreach (var repository in Repositories)
        {
            if (repository.InstallationId <= 0 || !Regex.IsMatch(repository.Owner, "\\A[A-Za-z0-9-]{1,39}\\z") || !Regex.IsMatch(repository.Name, "\\A[A-Za-z0-9_.-]{1,100}\\z") || repository.Name is "." or ".." || !Regex.IsMatch(repository.DefaultBranch, "\\A[A-Za-z0-9/_-]{1,100}\\z") || repository.DefaultBranch.Contains("..")) throw new ArgumentException("Invalid authorized repository.");
            if (repository.TestRemotePath != null && (!testing || !Path.GetFullPath(repository.TestRemotePath).StartsWith(Path.GetFullPath(StateDirectory).TrimEnd('/') + "/fixtures/", StringComparison.Ordinal))) throw new ArgumentException("File remotes are restricted to isolated test fixtures, never production.");
            foreach (var profile in repository.Profiles.Values)
            {
                if (!Regex.IsMatch(profile.Image, "\\Asha256:[a-f0-9]{64}\\z") || profile.Command.Length == 0 || profile.Command.Any(x => x.Length > 1000 || x.Contains('\0')) || profile.MemoryMb is < 128 or > 4096 || profile.Cpus is < 1 or > 4 || profile.TimeoutSeconds is < 1 or > 300) throw new ArgumentException("Profiles require a pinned image ID, fixed command and bounded resources.");
            }
        }
    }
}
public sealed class AuthorizedRepository
{
    public string Owner { get; set; } = "";
    public string Name { get; set; } = "";
    public long InstallationId { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public string? TestRemotePath { get; set; }
    public Dictionary<string, ExecutionProfile> Profiles { get; set; } = new(StringComparer.Ordinal);
}
public sealed class ExecutionProfile
{
    public string Image { get; set; } = "";
    public string[] Command { get; set; } = [];
    public string[] RequiredFiles { get; set; } = [];
    public int MemoryMb { get; set; } = 1024;
    public int Cpus { get; set; } = 1;
    public int TimeoutSeconds { get; set; } = 120;
}
