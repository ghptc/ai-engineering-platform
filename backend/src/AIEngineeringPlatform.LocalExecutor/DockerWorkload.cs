using AIEngineeringPlatform.Application;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace AIEngineeringPlatform.LocalExecutor;

public sealed class DockerWorkload(ExecutorOptions options, ControlledProcess process)
{
    private bool cleanupFaulted;
    public string OwnershipLabel => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(options.StateDirectory))).ToLowerInvariant()[..24];
    public async Task RecoverAsync(CancellationToken ct)
    {
        await VerifyHostAsync(ct);
        var existing = await process.RunAsync("/usr/bin/docker", ["ps", "-aq", "--filter", "label=aep.executor=" + OwnershipLabel], null, 15, 10000, ct);
        if (existing.ExitCode != 0) throw new InvalidOperationException("Could not enumerate abandoned executor workloads.");
        foreach (var id in existing.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Regex.IsMatch(id, "\\A[a-f0-9]{12,64}\\z")) throw new InvalidOperationException("Unexpected container identity.");
            var cleanup = await process.RunAsync("/usr/bin/docker", ["rm", "-f", "-v", id], null, 15, 10000, ct);
            if (cleanup.ExitCode != 0) throw new InvalidOperationException("Cannot clean up an abandoned executor workload.");
        }
    }
    public async Task VerifyHostAsync(CancellationToken ct)
    {
        if (cleanupFaulted) throw new InvalidOperationException("Executor cleanup failed; restart after inspecting owned workloads.");
        var result = await process.RunAsync("/usr/bin/docker", ["info", "--format", "{{json .}}"], null, 15, 200000, ct);
        if (result.ExitCode != 0) throw new InvalidOperationException("Local Docker is unavailable.");
        using var info = JsonDocument.Parse(result.Output);
        var root = info.RootElement;
        if (root.GetProperty("OSType").GetString() != "linux" || root.GetProperty("CgroupVersion").GetString() != "2" || !root.GetProperty("MemoryLimit").GetBoolean() || !root.GetProperty("PidsLimit").GetBoolean() || !root.GetProperty("CpuCfsQuota").GetBoolean() || !root.GetProperty("SecurityOptions").EnumerateArray().Any(x => x.GetString()!.StartsWith("name=seccomp", StringComparison.Ordinal))) throw new InvalidOperationException("Docker isolation requires Linux, cgroup v2, CPU/memory/PID enforcement and seccomp.");
    }
    public async Task<ProcessResult> ExecuteAsync(string workspace, ExecutionProfile profile, SandboxRequest request, CancellationToken ct)
    {
        await VerifyHostAsync(ct);
        var name = "aep-job-" + Guid.NewGuid().ToString("N");
        var archive = WorkspaceFiles.Archive(workspace, options.WorkspaceLimitMb);
        var memory = Math.Min(profile.MemoryMb, request.MemoryMb);
        var seconds = Math.Min(request.TimeoutSeconds, profile.TimeoutSeconds);
        string[] arguments = ["create", "--pull", "never", "--name", name, "--label", "aep.executor=" + OwnershipLabel, "--label", "aep.run=" + request.RunId,
            "--interactive", "--ipc", "private", "--cgroupns", "private", "--network", "none", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges=true",
            "--user", "65532:65532", "--pids-limit", "128", "--cpus", Math.Min(request.CpuLimit, profile.Cpus).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--memory", memory + "m", "--memory-swap", memory + "m", "--ulimit", "nofile=1024:1024", "--ulimit", "core=0:0",
            "--tmpfs", "/workspace:rw,exec,nosuid,nodev,size=256m,uid=65532,gid=65532,mode=0700", "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=256m,uid=65532,gid=65532,mode=0700",
            "--workdir", "/workspace", "--env", "HOME=/tmp", "--env", "DOTNET_CLI_HOME=/tmp", "--env", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1", "--env", "DOTNET_CLI_TELEMETRY_OPTOUT=1",
            "--log-driver", "local", "--log-opt", "max-size=1m", "--log-opt", "max-file=1", "--log-opt", "compress=false", "--entrypoint", "/usr/local/bin/aep-run", profile.Image, .. profile.Command];
        try
        {
            var create = await process.RunAsync("/usr/bin/docker", arguments, null, 15, 10000, ct);
            if (create.ExitCode != 0) throw new InvalidOperationException("Could not create isolated Docker workload: " + create.Output);
            var inspection = await process.RunAsync("/usr/bin/docker", ["inspect", name], null, 10, 100000, ct);
            if (inspection.ExitCode != 0) throw new InvalidOperationException("Cannot verify container configuration.");
            VerifyContainer(inspection.Output, profile.Image, memory);
            var execution = await process.RunAsync("/usr/bin/docker", ["start", "--attach", "--interactive", name], null, seconds, request.OutputLimitBytes, ct, archive);
            if (execution.TimedOut || execution.OutputExceeded) return execution;
            var state = await process.RunAsync("/usr/bin/docker", ["inspect", name, "--format", "{{.State.ExitCode}}"], null, 10, 1000, ct);
            if (state.ExitCode != 0 || !int.TryParse(state.Output.Trim(), out var actualExit)) throw new InvalidOperationException("Container exit evidence is unavailable.");
            return execution with { ExitCode = actualExit };
        }
        finally
        {
            // Independent cleanup token: a cancelled HTTP request must not leave a workload alive.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var removal = await process.RunAsync("/usr/bin/docker", ["rm", "--force", "--volumes", name], null, 10, 10000, cleanup.Token);
            if (removal.ExitCode != 0 && !removal.Output.Contains("No such container", StringComparison.OrdinalIgnoreCase)) { cleanupFaulted = true; throw new InvalidOperationException("Workload cleanup failed; executor must be checked before reuse."); }
        }
    }
    public static void VerifyContainer(string json, string image, int memory)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement[0];
        var host = root.GetProperty("HostConfig"); var config = root.GetProperty("Config");
        bool Empty(string name) => !host.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null || value.GetArrayLength() == 0;
        if (root.GetProperty("Image").GetString() != image || config.GetProperty("User").GetString() != "65532:65532" || host.GetProperty("Privileged").GetBoolean() || !host.GetProperty("ReadonlyRootfs").GetBoolean() || host.GetProperty("NetworkMode").GetString() != "none" || host.GetProperty("Memory").GetInt64() != memory * 1024L * 1024 || host.GetProperty("MemorySwap").GetInt64() != memory * 1024L * 1024 || host.GetProperty("PidsLimit").GetInt64() != 128 || host.GetProperty("NanoCpus").GetInt64() is < 1 or > 4000000000L || !Empty("Binds") || !Empty("Devices") || !Empty("CapAdd") || root.GetProperty("Mounts").GetArrayLength() != 0 || host.GetProperty("PidMode").GetString() == "host" || host.GetProperty("IpcMode").GetString() == "host" || !host.GetProperty("CapDrop").EnumerateArray().Any(x => x.GetString() == "ALL") || !host.GetProperty("SecurityOpt").EnumerateArray().Any(x => x.GetString() == "no-new-privileges=true")) throw new InvalidOperationException("Docker container isolation verification failed.");
        var mounts = host.GetProperty("Tmpfs");
        if (mounts.EnumerateObject().Count() != 2 || !mounts.TryGetProperty("/workspace", out var workspace) || !mounts.TryGetProperty("/tmp", out var temp) || !workspace.GetString()!.Contains("size=256m", StringComparison.Ordinal) || !temp.GetString()!.Contains("size=256m", StringComparison.Ordinal)) throw new InvalidOperationException("Bounded temporary filesystems are required.");
    }
    public static int TestCount(string output)
    {
        output = Regex.Replace(output, @"\x1B\[[0-9;]*m", "");
        var patterns = new[] { @"(?m)^(?:#|ℹ) tests\s+(\d+)\s*$", @"Total:\s*(\d+)", @"Total tests:\s*(\d+)", @"Ran\s+(\d+)\s+test", @"Tests\s+(\d+)\s+passed" };
        return patterns.SelectMany(pattern => Regex.Matches(output, pattern, RegexOptions.CultureInvariant).Select(match => int.Parse(match.Groups[1].Value))).DefaultIfEmpty(0).Max();
    }
}
