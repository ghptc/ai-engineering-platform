using AIEngineeringPlatform.Infrastructure;
using System.Net.Sockets;
using System.Text;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;

public sealed class UnixReadinessTests
{
    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"contractVersion\":1}", false)]
    [InlineData("{\"contractVersion\":\"2\"}", false)]
    [InlineData("{\"contractVersion\":2}", true)]
    public async Task OutdatedDaemonCannotAdvertiseImplementationReadiness(string body, bool expected)
    {
        var path = Path.Combine(Path.GetTempPath(), "aep-readiness-" + Guid.NewGuid().ToString("N") + ".sock");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path)); listener.Listen(1);
        var serving = Task.Run(async () => {
            using var socket = await listener.AcceptAsync(timeout.Token);
            await using var stream = new NetworkStream(socket);
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            for (var lines = 0; lines < 32; lines++)
                if (string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) break;
            var response = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
            await stream.WriteAsync(response, timeout.Token);
        });
        try
        {
            using var sandbox = new UnixExecutionSandbox(path, new string('x', 64));
            Assert.Equal(expected, await sandbox.IsReadyAsync(timeout.Token));
            await serving;
        }
        finally { listener.Dispose(); File.Delete(path); }
    }
}
