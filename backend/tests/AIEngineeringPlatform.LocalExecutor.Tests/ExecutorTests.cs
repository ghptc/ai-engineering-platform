using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using System.Diagnostics;
using System.Security.Cryptography;
using AIEngineeringPlatform.LocalExecutor;
using System.Text.Json;
using Xunit;
namespace AIEngineeringPlatform.LocalExecutor.Tests;

public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute() { if (Environment.GetEnvironmentVariable("RUN_LOCAL_EXECUTOR_TESTS") != "1") Skip = "Opt in with RUN_LOCAL_EXECUTOR_TESTS=1 and EXECUTOR_TEST_IMAGE=<pinned image ID>; these tests run real Docker workloads."; }
}
public sealed class ExecutorTests
{
    [Theory]
    [InlineData("ℹ tests 1\nℹ pass 1\nℹ fail 0\n", 1)]
    [InlineData("# tests 2\n# pass 2\n", 2)]
    [InlineData("\u001b[32mℹ tests 1\u001b[0m\n", 1)]
    [InlineData("ℹ tests 0\n", 0)]
    [InlineData("arbitrary output, no tests executed", 0)]
    public void NodeTestReporterCountsAreRecognizedWithoutAcceptingZeroTests(string output, int expected)
        => Assert.Equal(expected, DockerWorkload.TestCount(output));
    [Theory]
    [InlineData("../escape")]
    [InlineData("/etc/passwd")]
    [InlineData(".git/config")]
    [InlineData(".env.local")]
    [InlineData("private.pem")]
    public void PathsRejectSecretsAndTraversal(string path) => Assert.Throws<ArgumentException>(() => WorkspaceFiles.Resolve("/tmp/workspace", path));
    [Fact] public void NetworkingIsNeverModelControlled() => Assert.Throws<ArgumentException>(() => LocalExecutionService.ValidateRequest(Request("run", "op", "RunTests", new { profile = "all" }) with { NetworkEnabled = true }));
    [Fact]
    public void ConfigRequiresPinnedImagesAndRejectsProductionFileRemotes()
    {
        var options = new ExecutorOptions { StateDirectory = "/tmp/aep-unit", Repositories = [new() { Owner = "owner", Name = "repo", InstallationId = 1, TestRemotePath = "/tmp/aep-unit/fixtures/bare" }] };
        Assert.Throws<ArgumentException>(() => options.Validate());
        options.Repositories[0].TestRemotePath = null;
        options.Repositories[0].Profiles["all"] = new() { Image = "node:latest", Command = ["node", "--test"] };
        Assert.Throws<ArgumentException>(() => options.Validate());
    }
    [Fact]
    public void PatchesRequireUniqueMatchesAndRevisionIncludesNewFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "aep-path-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            WorkspaceFiles.Write(root, "feature.txt", "one one");
            Assert.Throws<ArgumentException>(() => WorkspaceFiles.Patch(root, "feature.txt", "one", "two"));
            var before = WorkspaceFiles.Revision(root, 1);
            WorkspaceFiles.Write(root, "new.txt", "new source"); Assert.NotEqual(before, WorkspaceFiles.Revision(root, 1));
            File.CreateSymbolicLink(Path.Combine(root, "escape"), "/etc/passwd");
            Assert.Throws<ArgumentException>(() => WorkspaceFiles.Resolve(root, "escape"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [DockerFact]
    public async Task ExistingFeatureBranchIsVerifiedWithoutChangingDefaultBranch()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Options.Repositories[0].Profiles["node"] = new() { Image = Environment.GetEnvironmentVariable("EXECUTOR_TEST_IMAGE")!, Command = ["node", "--test"] };
        var executor = fixture.Executor;
        await executor.ExecuteAsync(Request("original", "source", "WriteFile", new { path = "feature.mjs", content = "export function answer() { return 2; }\n" }), default);
        await executor.ExecuteAsync(Request("original", "test", "ApplyPatch", new { patch = JsonSerializer.Serialize(new { path = "feature.test.mjs", search = "answer(), 1", replacement = "answer(), 2" }) }), default);
        var verified = await executor.ExecuteAsync(Request("original", "tests", "RunTests", new { profile = "node" }), default);
        Assert.True(verified.Success, verified.Output);
        var commit = await executor.ExecuteAsync(Request("original", "commit", "CreateCommit", new { branch = "engineering/original", message = "fix: answer returns 2", expectedRevision = verified.DiffHash }), default);
        await executor.ExecuteAsync(Request("original", "push", "PushBranch", new { branch = "engineering/original" }), default);
        using var json = JsonDocument.Parse(commit.Output);
        var sha = json.RootElement.GetProperty("sha").GetString();
        var initial = await executor.ExecuteAsync(Request("repeat", "default", "ReadFile", new { path = "feature.mjs" }), default);
        Assert.Contains("return 1", initial.Output);
        await executor.ExecuteAsync(Request("repeat", "select", "SelectBranch", new { branch = "engineering/original", commit = sha }), default);
        var selected = await executor.ExecuteAsync(Request("repeat", "selected", "ReadFile", new { path = "feature.mjs" }), default);
        Assert.Contains("return 2", selected.Output);
        var facts = await executor.ExecuteAsync(Request("repeat", "facts", "ReadRepository", new { }), default);
        using var inventory = JsonDocument.Parse(facts.Output);
        Assert.False(inventory.RootElement.GetProperty("HasSourceChanges").GetBoolean());
        var diff = await executor.ExecuteAsync(Request("repeat", "diff", "InspectDiff", new { }), default);
        Assert.Contains("return 2", diff.Output);
        var tests = await executor.ExecuteAsync(Request("repeat", "tests", "RunTests", new { profile = "node" }), default);
        Assert.True(tests.Success, tests.Output); Assert.Equal(diff.DiffHash, tests.DiffHash); Assert.Equal(verified.DiffHash, tests.DiffHash);
        var main = await fixture.Process.RunAsync("/usr/bin/git", ["--git-dir", fixture.Remote, "rev-parse", "refs/heads/main"], null, 10, 10000, default);
        Assert.Equal(fixture.OriginalHead, main.Output.Trim());
        await Assert.ThrowsAsync<ArgumentException>(() => executor.ExecuteAsync(Request("repeat", "unsafe", "SelectBranch", new { branch = "main", commit = sha }), default));
    }
    [DockerFact]
    public async Task RealDisposableRepositoryCompletesIsolatedNodeAndDotnetCycle()
    {
        await using var fixture = await Fixture.CreateAsync();
        var executor = fixture.Executor; var ct = CancellationToken.None;
        var before = await executor.ExecuteAsync(Request("cycle", "before", "InspectDiff", new { }), ct); Assert.Equal("", before.Output);
        var files = await executor.ExecuteAsync(Request("cycle", "list", "ListFiles", new { }), ct); Assert.Contains("feature.mjs", files.Output);
        await executor.ExecuteAsync(Request("cycle", "write", "WriteFile", new { path = "feature.mjs", content = "export function answer() { return 2; }\n" }), ct);
        await executor.ExecuteAsync(Request("cycle", "patch", "ApplyPatch", new { patch = JsonSerializer.Serialize(new { path = "feature.test.mjs", search = "answer(), 1", replacement = "answer(), 2" }) }), ct);
        await executor.ExecuteAsync(Request("cycle", "backend-write", "WriteFile", new { path = "Feature.cs", content = "public static class Feature { public static int Answer() => 2; }" }), ct);
        await executor.ExecuteAsync(Request("cycle", "backend-test-patch", "ApplyPatch", new { patch = JsonSerializer.Serialize(new { path = "Program.cs", search = "Feature.Answer() != 1", replacement = "Feature.Answer() != 2" }) }), ct);
        var diff = await executor.ExecuteAsync(Request("cycle", "diff", "InspectDiff", new { }), ct); Assert.Contains("return 2", diff.Output);
        var tests = await executor.ExecuteAsync(Request("cycle", "tests", "RunTests", new { profile = "all" }), ct);
        Assert.True(tests.Success, tests.Output); Assert.Equal(0, tests.ExitCode); Assert.Contains("# tests 1", tests.Output); Assert.Contains("C# assertion executed", tests.Output); Assert.Equal(diff.DiffHash, tests.DiffHash);
        var commit = await executor.ExecuteAsync(Request("cycle", "commit", "CreateCommit", new { branch = "engineering/cycle", message = "Implement fixture feature", expectedRevision = tests.DiffHash }), ct); Assert.True(commit.Success);
        var replay = await executor.ExecuteAsync(Request("cycle", "commit", "CreateCommit", new { branch = "engineering/cycle", message = "Implement fixture feature", expectedRevision = tests.DiffHash }) with { Fence = 2, LeaseOwner = "recovered-worker" }, ct);
        Assert.Equal(commit, replay);
        var push = await executor.ExecuteAsync(Request("cycle", "push", "PushBranch", new { branch = "engineering/cycle" }) with { Fence = 2, LeaseOwner = "recovered-worker" }, ct); Assert.True(push.Success);
        var branch = await fixture.Process.RunAsync("/usr/bin/git", ["--git-dir", fixture.Remote, "rev-parse", "refs/heads/engineering/cycle"], null, 10, 10000, ct);
        Assert.Equal(0, branch.ExitCode);
        var main = await fixture.Process.RunAsync("/usr/bin/git", ["--git-dir", fixture.Remote, "rev-parse", "refs/heads/main"], null, 10, 10000, ct); Assert.Equal(fixture.OriginalHead, main.Output.Trim());
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(Request("cycle", "stale", "ListFiles", new { }), ct));
        await executor.ExecuteAsync(Request("cycle", "edit-after-test", "WriteFile", new { path = "feature.mjs", content = "export function answer() { return 3; }" }) with { Fence = 2, LeaseOwner = "recovered-worker" }, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(Request("cycle", "unsafe-commit", "CreateCommit", new { branch = "engineering/cycle", message = "Unsafe", expectedRevision = tests.DiffHash }) with { Fence = 2, LeaseOwner = "recovered-worker" }, ct));
    }
    [DockerFact]
    public async Task RealDockerEnforcesIsolationTimeoutOutputLimitsAndCleanup()
    {
        await using var fixture = await Fixture.CreateAsync();
        var isolation = await fixture.Executor.ExecuteAsync(Request("isolation", "isolation-check", "RunCommand", new { profile = "build" }), default); Assert.True(isolation.Success, isolation.Output); Assert.Contains("isolation verified", isolation.Output);
        var timeout = await fixture.Executor.ExecuteAsync(Request("isolation", "timeout", "RunCommand", new { profile = "lint" }) with { TimeoutSeconds = 1 }, default); Assert.False(timeout.Success); Assert.Equal(124, timeout.ExitCode);
        var flood = await fixture.Executor.ExecuteAsync(Request("isolation", "flood", "RunCommand", new { profile = "format" }) with { OutputLimitBytes = 1024 }, default); Assert.False(flood.Success); Assert.Equal(125, flood.ExitCode);
        await Assert.ThrowsAsync<ConfigurationException>(() => fixture.Executor.ExecuteAsync(Request("isolation", "zero-tests", "RunTests", new { profile = "frontend" }), default));
        var containers = await fixture.Process.RunAsync("/usr/bin/docker", ["ps", "-aq", "--filter", "label=aep.executor"], null, 10, 10000, default); Assert.Equal("", containers.Output.Trim());
    }
    [DockerFact]
    public async Task RealUnixDaemonRequiresAuthenticationAndSurvivesRestart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var socket = Path.Combine(fixture.Root, "socket", "executor.sock");
        var tokenFile = Path.Combine(fixture.Root, "control-token");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await File.WriteAllTextAsync(tokenFile, token); File.SetUnixFileMode(tokenFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        fixture.Options.SocketPath = socket; fixture.Options.TokenFile = tokenFile;
        var config = Path.Combine(fixture.Root, "executor.json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { Executor = fixture.Options }));
        var entry = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/AIEngineeringPlatform.LocalExecutor/bin/Debug/net10.0/AIEngineeringPlatform.LocalExecutor.dll"));
        async Task<Process> StartAsync()
        {
            var start = new ProcessStartInfo("dotnet", entry) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.Environment["EXECUTOR_CONFIG"] = config; start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
            var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            for (var i = 0; i < 100; i++)
            {
                if (process.HasExited) throw new Exception("Daemon startup failed: " + await stderr + await stdout);
                if (File.Exists(socket)) { using var probe = new UnixExecutionSandbox(socket, token); if (await probe.IsReadyAsync(default)) return process; }
                await Task.Delay(50);
            }
            process.Kill(entireProcessTree: true); throw new TimeoutException("Executor socket not created.");
        }
        using var daemon = await StartAsync();
        SandboxResponse first;
        try
        {
            using var client = new UnixExecutionSandbox(socket, token);
            Assert.True(await client.IsReadyAsync(default));
            using var unauthorized = new UnixExecutionSandbox(socket, new string('x', 64));
            Assert.False(await unauthorized.IsReadyAsync(default));
            first = await client.ExecuteAsync(Request("socket-run", "read", "ReadRepository", new { }), default); Assert.True(first.Success);
            var rejected = await client.ExecuteAsync(Request("socket-run", "bad-read", "ReadFile", new { path = "missing.cs" }), default); Assert.False(rejected.Success); Assert.Contains("Executor rejected", rejected.Output);
            var corrected = await client.ExecuteAsync(Request("socket-run", "corrected-read", "ReadFile", new { path = "Feature.cs" }), default); Assert.True(corrected.Success);
        }
        finally { daemon.Kill(entireProcessTree: true); await daemon.WaitForExitAsync(); }
        using var restarted = await StartAsync();
        try
        {
            using var client = new UnixExecutionSandbox(socket, token);
            var replay = await client.ExecuteAsync(Request("socket-run", "read", "ReadRepository", new { }) with { Fence = 2, LeaseOwner = "restart-worker" }, default);
            Assert.Equal(first, replay);
        }
        finally { restarted.Kill(entireProcessTree: true); await restarted.WaitForExitAsync(); }
    }
    private static SandboxRequest Request(string run, string operation, string tool, object args) => new(operation, run, new("test-owner", "aep-fixture", 1), tool, JsonSerializer.SerializeToElement(args), TimeoutSeconds: 120, Fence: 1, LeaseOwner: "test-worker");
    private sealed class Fixture : IAsyncDisposable
    {
        public required string Root; public required string Remote; public required ControlledProcess Process; public required LocalExecutionService Executor; public required string OriginalHead; public required ExecutorOptions Options;
        public static async Task<Fixture> CreateAsync()
        {
            var image = Environment.GetEnvironmentVariable("EXECUTOR_TEST_IMAGE") ?? throw new InvalidOperationException("EXECUTOR_TEST_IMAGE is required for opted-in real tests.");
            var root = Path.Combine(Path.GetTempPath(), "aep-executor-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root, "control-home"));
            var remote = Path.Combine(root, "fixtures", "remote.git"); Directory.CreateDirectory(Path.GetDirectoryName(remote)!);
            var repository = new AuthorizedRepository { Owner = "test-owner", Name = "aep-fixture", InstallationId = 1, TestRemotePath = remote };
            repository.Profiles["all"] = new() { Image = image, RequiredFiles = ["fixture.csproj", "feature.test.mjs"], Command = ["python3", "-c", "import subprocess; subprocess.run(['dotnet','run','--project','fixture.csproj','--no-launch-profile'],check=True); subprocess.run(['node','--test','--test-reporter=tap','feature.test.mjs'],check=True)"] };
            repository.Profiles["frontend"] = new() { Image = image, Command = ["node", "-e", "console.log('no tests executed')"] };
            repository.Profiles["lint"] = new() { Image = image, Command = ["node", "-e", "setTimeout(()=>{},60000)"] };
            repository.Profiles["format"] = new() { Image = image, Command = ["node", "-e", "process.stdout.write('x'.repeat(200000))"] };
            repository.Profiles["build"] = new() { Image = image, Command = ["python3", "-c", "import os,pathlib,socket; assert os.getuid()==65532; assert pathlib.Path('/sys/fs/cgroup/memory.max').read_text().strip()=='536870912'; assert pathlib.Path('/sys/fs/cgroup/pids.max').read_text().strip()=='128'; assert os.statvfs('/workspace').f_blocks*os.statvfs('/workspace').f_frsize<=268435456; s=pathlib.Path('/proc/self/status').read_text(); assert 'NoNewPrivs:\\t1' in s and 'Seccomp:\\t2' in s; assert not pathlib.Path('/var/run/docker.sock').exists(); assert not pathlib.Path('/home/gabriel').exists(); assert not any('SECRET' in k or 'TOKEN' in k or 'OPENAI' in k or 'MONGO' in k for k in os.environ);\ntry: pathlib.Path('/etc/escape').write_text('bad'); raise AssertionError('root filesystem writable')\nexcept OSError: pass\ntry: socket.create_connection(('1.1.1.1',53),timeout=1); raise AssertionError('network enabled')\nexcept OSError: pass\nprint('isolation verified')"] };
            var options = new ExecutorOptions { StateDirectory = root, Repositories = [repository] }; options.Validate(testing: true);
            var process = new ControlledProcess(options);
            async Task Git(string? directory, params string[] args) { var result = await process.RunAsync("/usr/bin/git", args, directory, 15, 10000, default); if (result.ExitCode != 0) throw new Exception(result.Output); }
            await Git(null, "init", "--bare", "--initial-branch=main", remote);
            var seed = Path.Combine(root, "seed"); Directory.CreateDirectory(seed); await Git(seed, "init", "--initial-branch=main");
            File.WriteAllText(Path.Combine(seed, "feature.mjs"), "export function answer() { return 1; }\n");
            File.WriteAllText(Path.Combine(seed, "feature.test.mjs"), "import {test} from 'node:test'; import assert from 'node:assert/strict'; import {answer} from './feature.mjs'; test('answer',()=>assert.equal(answer(), 1));\n");
            File.WriteAllText(Path.Combine(seed, "fixture.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(seed, "Feature.cs"), "public static class Feature { public static int Answer() => 1; }");
            File.WriteAllText(Path.Combine(seed, "Program.cs"), "if (Feature.Answer() != 1) throw new System.Exception(\"Assertion failed\"); System.Console.WriteLine(\"C# assertion executed; Total: 1\");");
            await Git(seed, "add", "."); await Git(seed, "-c", "user.name=Executor Test", "-c", "user.email=test@localhost", "commit", "-m", "Create disposable test fixture"); await Git(seed, "remote", "add", "origin", remote); await Git(seed, "push", "origin", "main");
            var head = await process.RunAsync("/usr/bin/git", ["rev-parse", "HEAD"], seed, 10, 10000, default);
            return new() { Root = root, Remote = remote, Process = process, Executor = new(options, process, new(options, process)), OriginalHead = head.Output.Trim(), Options = options };
        }
        public ValueTask DisposeAsync() { Directory.Delete(Root, recursive: true); return ValueTask.CompletedTask; }
    }
}
