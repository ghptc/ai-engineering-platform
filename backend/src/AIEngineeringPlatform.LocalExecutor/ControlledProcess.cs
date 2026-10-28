using System.Diagnostics;
using System.Text;
namespace AIEngineeringPlatform.LocalExecutor;

public sealed record ProcessResult(int ExitCode, string Output, bool TimedOut = false, bool OutputExceeded = false);
public sealed class ControlledProcess(ExecutorOptions options)
{
    public async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, string? directory, int seconds, int outputLimit, CancellationToken ct, byte[]? input = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        if (executable is not ("/usr/bin/git" or "/usr/bin/docker")) throw new UnauthorizedAccessException("Host executable is not registered.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        var start = new ProcessStartInfo(executable == "/usr/bin/git" ? "/usr/bin/prlimit" : executable) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false, WorkingDirectory = directory ?? options.StateDirectory };
        if (executable == "/usr/bin/git") foreach (var limit in new[] { "--fsize=67108864", "--as=1073741824", "--", executable }) start.ArgumentList.Add(limit);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Clear();
        start.Environment["PATH"] = "/usr/bin:/bin";
        start.Environment["HOME"] = Path.Combine(options.StateDirectory, "control-home");
        start.Environment["DOCKER_HOST"] = options.DockerHost;
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["LC_ALL"] = "C.UTF-8";
        if (environment != null) foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start controlled host operation.");
        var output = new StringBuilder(); var gate = new object(); var count = 0; var exceeded = false;
        async Task ReadAsync(StreamReader reader)
        {
            var buffer = new char[4096]; int length;
            while ((length = await reader.ReadAsync(buffer.AsMemory(), timeout.Token)) > 0)
            {
                lock (gate)
                {
                    var text = new string(buffer, 0, length); count += Encoding.UTF8.GetByteCount(text);
                    if (count > outputLimit) { exceeded = true; timeout.Cancel(); return; }
                    output.Append(text);
                }
            }
        }
        async Task WriteAsync()
        {
            try { if (input != null) await process.StandardInput.BaseStream.WriteAsync(input, timeout.Token); }
            catch (IOException) { /* Child may close stdin after rejecting its input. */ }
            finally { process.StandardInput.Close(); }
        }
        try
        {
            await Task.WhenAll(ReadAsync(process.StandardOutput), ReadAsync(process.StandardError), WriteAsync(), process.WaitForExitAsync(timeout.Token));
            return new(process.ExitCode, output.ToString());
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            ct.ThrowIfCancellationRequested();
            return new(exceeded ? 125 : 124, output.ToString(), !exceeded, exceeded);
        }
    }
}
