using AIEngineeringPlatform.LocalExecutor;
using Xunit;
namespace AIEngineeringPlatform.LocalExecutor.Tests;
public sealed class ManagedIpcTests
{
    [Fact]
    public void ComposeExecutorUsesApplicationIpcGroupWithoutAdditionalCapabilities()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var compose = File.ReadAllText(Path.Combine(root, "compose.yaml"));
        var executor = compose.Split("  executor:\n")[1].Split("  api:\n")[0];
        Assert.Contains("user: \"0:1654\"", executor);
        Assert.Contains("cap_add: [CHOWN, FOWNER, DAC_OVERRIDE]", executor);
        Assert.DoesNotContain("FSETID", executor.Split("cap_add:")[1]);
        Assert.Contains("/var/run/docker.sock:/var/run/docker.sock", executor);
        Assert.DoesNotContain("/var/run/docker.sock", compose.Split("  api:\n")[1]);
    }
    [DockerFact]
    public async Task RealRestrictedContainerInheritsSocketGroupOnlyWhenPrimaryGroupMatches()
    {
        var image = Environment.GetEnvironmentVariable("EXECUTOR_TEST_IMAGE")!;
        var process = new ControlledProcess(new() { StateDirectory = Path.GetTempPath() });
        const string script = "import os,socket,stat,tempfile; p=tempfile.mkdtemp(dir='/tmp'); os.chown(p,0,1654); os.chmod(p,0o2750); s=socket.socket(socket.AF_UNIX); s.bind(p+'/ipc.sock'); os.chmod(p+'/ipc.sock',0o660); st=os.stat(p+'/ipc.sock'); print('group='+str(st.st_gid)+';mode='+oct(stat.S_IMODE(st.st_mode)))";
        foreach (var (identity, expected) in new[] { ("0:0", "group=0;mode=0o660"), ("0:1654", "group=1654;mode=0o660") })
        {
            var result = await process.RunAsync("/usr/bin/docker", ["run", "--rm", "--network", "none", "--read-only", "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=8m", "--user", identity, "--cap-drop", "ALL", "--cap-add", "CHOWN", "--cap-add", "FOWNER", "--cap-add", "DAC_OVERRIDE", "--security-opt", "no-new-privileges:true", "--entrypoint", "python3", image, "-c", script], Path.GetTempPath(), 30, 10000, default);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains(expected, result.Output);
        }
    }
}
