using System.Net.Http.Json;
using AIEngineeringPlatform.Application;
namespace AIEngineeringPlatform.Infrastructure;

public sealed class RemoteSandboxOptions { public string Provider { get; set; } = "Remote"; public string? Endpoint { get; set; } public string? Token { get; set; } public string? SocketPath { get; set; } }
public sealed class RemoteExecutionSandbox(HttpClient http, string? endpoint, string? token, bool unixTransport = false) : IExecutionSandbox
{
    public bool Configured => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || unixTransport && endpoint == "http://local-executor.invalid/") && string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrWhiteSpace(token);
    public async Task<SandboxResponse> ExecuteAsync(SandboxRequest request, CancellationToken ct)
    {
        if (!Configured) throw new ConfigurationException("Secure remote execution sandbox is not configured. Host execution is disabled.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 120) + 10));
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(endpoint!), "v1/operations")) { Content = JsonContent.Create(request) };
        message.Headers.Authorization = new("Bearer", token);
        message.Headers.Add("Idempotency-Key", request.OperationId);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadAsStringAsync(timeout.Token);
            if (problem.Length > 10000) problem = "Executor rejected tool arguments.";
            return new(false, problem, "rejected-" + request.OperationId, null, request.RunId);
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192]; int length;
        while ((length = await stream.ReadAsync(bytes, timeout.Token)) > 0) { if (buffer.Length + length > 200000) throw new InvalidOperationException("Sandbox response exceeds size limit."); await buffer.WriteAsync(bytes.AsMemory(0, length), timeout.Token); }
        var result = System.Text.Json.JsonSerializer.Deserialize<SandboxResponse>(buffer.ToArray(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Sandbox returned no result.");
        if (string.IsNullOrWhiteSpace(result.EvidenceId) || result.WorkspaceId != request.RunId || result.Output is null || System.Text.Encoding.UTF8.GetByteCount(result.Output) > request.OutputLimitBytes) throw new InvalidOperationException("Sandbox evidence or workspace identity is invalid.");
        return result;
    }
}
