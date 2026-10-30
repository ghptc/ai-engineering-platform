using AIEngineeringPlatform.Application;
using System.Net.Sockets;
namespace AIEngineeringPlatform.Infrastructure;

public sealed class UnixExecutionSandbox : IExecutionSandbox, IDisposable
{
    private readonly string socketPath;
    private readonly HttpClient client;
    private readonly RemoteExecutionSandbox adapter;
    public UnixExecutionSandbox(string socketPath, string token)
    {
        if (!Path.IsPathFullyQualified(socketPath) || token.Length < 32) throw new ConfigurationException("LocalUnix requires an absolute socket path and a random token of at least 32 characters.");
        this.socketPath = socketPath;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            }
        };
        client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(140) };
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        // Transport is exclusively the validated Unix socket; this URI is never resolved on a network.
        adapter = new RemoteExecutionSandbox(client, "http://local-executor.invalid/", token, unixTransport: true);
    }
    public bool Configured => File.Exists(socketPath);
    public Task<SandboxResponse> ExecuteAsync(SandboxRequest request, CancellationToken ct) => Configured ? adapter.ExecuteAsync(request, ct) : throw new ConfigurationException("Local executor socket is unavailable. Start the host daemon.");
    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        if (!Configured) return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://local-executor.invalid/health/ready");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return false;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var bytes = new byte[4097]; var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), ct);
                if (read == 0) break;
                count += read;
            }
            if (count > 4096) return false;
            using var document = System.Text.Json.JsonDocument.Parse(bytes.AsMemory(0, count));
            return document.RootElement.TryGetProperty("contractVersion", out var version) && version.ValueKind == System.Text.Json.JsonValueKind.Number && version.TryGetInt32(out var value) && value == 2;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested && (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException)) { return false; }
    }
    public void Dispose() => client.Dispose();
}
