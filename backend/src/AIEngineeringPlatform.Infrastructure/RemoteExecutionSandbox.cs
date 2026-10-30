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
        try { return await ExecuteCoreAsync(request, ct); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ExecutorFailureException(ExecutorFailureCategory.Timeout, request.OperationId, request.Operation, null, true); }
        catch (HttpRequestException) { throw new ExecutorFailureException(ExecutorFailureCategory.Infrastructure, request.OperationId, request.Operation, null, true); }
        catch (System.IO.IOException) { throw new ExecutorFailureException(ExecutorFailureCategory.Infrastructure, request.OperationId, request.Operation, null, true); }
        catch (System.Text.Json.JsonException) { throw new ExecutorFailureException(ExecutorFailureCategory.Infrastructure, request.OperationId, request.Operation, null, true); }
        catch (InvalidOperationException) { throw new ExecutorFailureException(ExecutorFailureCategory.Infrastructure, request.OperationId, request.Operation, null, true); }
    }
    private async Task<SandboxResponse> ExecuteCoreAsync(SandboxRequest request, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 120) + 10));
        using var response = await SendAsync(request, timeout.Token, ct);
        if (!response.IsSuccessStatusCode)
        {
            var (category, reason) = await ClassifyAsync(response, timeout.Token);
            throw new ExecutorFailureException(category, request.OperationId, request.Operation, (int)response.StatusCode,
                reason is ExecutorFailureReason.ImageUnavailable or ExecutorFailureReason.ProfileTargetMissing ? false : (int)response.StatusCode >= 500 || (int)response.StatusCode is 408 or 409 or 429, reason);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192]; int length;
        while ((length = await stream.ReadAsync(bytes, timeout.Token)) > 0) { if (buffer.Length + length > Math.Min(1_232_000L, request.OutputLimitBytes * 12L + 32000)) throw new InvalidOperationException("Sandbox response exceeds size limit."); await buffer.WriteAsync(bytes.AsMemory(0, length), timeout.Token); }
        var result = System.Text.Json.JsonSerializer.Deserialize<SandboxResponse>(buffer.ToArray(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Sandbox returned no result.");
        if (string.IsNullOrWhiteSpace(result.EvidenceId) || result.WorkspaceId != request.RunId || result.Output is null || System.Text.Encoding.UTF8.GetByteCount(result.Output) > request.OutputLimitBytes) throw new InvalidOperationException("Sandbox evidence or workspace identity is invalid.");
        if (result.Execution is { } execution && (execution.Stdout == null || execution.Stderr == null || System.Text.Encoding.UTF8.GetByteCount(execution.Stdout) + System.Text.Encoding.UTF8.GetByteCount(execution.Stderr) > request.OutputLimitBytes || execution.Command == null || execution.Command.Length > 32 || execution.Command.Any(x => x == null || x.Length > 1000) || execution.WorkingDirectory?.Length > 1024 || execution.Profile?.Length > 1000)) throw new InvalidOperationException("Sandbox execution details exceed bounded output limits.");
        return result;
    }
    private async Task<HttpResponseMessage> SendAsync(SandboxRequest request, CancellationToken timeout, CancellationToken caller)
    {
        // Only pure inspections can be retried without reconciling a possibly completed mutation.
        var inspection = request.Operation is "ReadRepository" or "ListFiles" or "ReadFile" or "SearchCode" or "InspectDiff";
        for (var attempt = 0; ; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(endpoint!), "v1/operations")) { Content = JsonContent.Create(request) };
            message.Headers.Authorization = new("Bearer", token);
            message.Headers.Add("Idempotency-Key", request.OperationId);
            try
            {
                var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout);
                if (inspection && attempt < Math.Clamp(request.MaxTransientRetries, 0, 2) && ((int)response.StatusCode is 429 or 502 or 504 || (int)response.StatusCode == 503 && response.Headers.RetryAfter != null))
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromMilliseconds(200 * (attempt + 1));
                    if (delay > TimeSpan.FromSeconds(2)) return response;
                    response.Dispose();
                    await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, timeout);
                    continue;
                }
                return response;
            }
            catch (HttpRequestException) { throw new ExecutorFailureException(ExecutorFailureCategory.Infrastructure, request.OperationId, request.Operation, null, true); }
            catch (OperationCanceledException) when (!caller.IsCancellationRequested) { throw new ExecutorFailureException(ExecutorFailureCategory.Timeout, request.OperationId, request.Operation, null, true); }
        }
    }

    private static async Task<(ExecutorFailureCategory, ExecutorFailureReason)> ClassifyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var category = (int)response.StatusCode switch
        {
            400 => ExecutorFailureCategory.InvalidArguments,
            401 or 403 => ExecutorFailureCategory.Authorization,
            404 => ExecutorFailureCategory.RepositoryAccess,
            409 => ExecutorFailureCategory.WorkspaceConflict,
            422 => ExecutorFailureCategory.ProfileRestriction,
            408 or 504 => ExecutorFailureCategory.Timeout,
            429 => ExecutorFailureCategory.RateLimited,
            _ => ExecutorFailureCategory.Infrastructure
        };
        // Retain at most 4096 bytes (one extra byte detects overflow). Unknown fields, HTML, details, paths and credentials are discarded.
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var bytes = new byte[4097]; var count = 0;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), ct);
            if (read == 0) break;
            count += read;
        }
        if (count > 4096) return (category, ExecutorFailureReason.Unspecified);
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(bytes.AsMemory(0, count));
            if (json.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return (category, ExecutorFailureReason.Unspecified);
            if (json.RootElement.TryGetProperty("code", out var code) && code.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                if (code.GetString() == "image_unavailable" && (int)response.StatusCode == 503) return (ExecutorFailureCategory.Infrastructure, ExecutorFailureReason.ImageUnavailable);
                if (code.GetString() == "profile_target_missing" && (int)response.StatusCode == 422) return (ExecutorFailureCategory.ProfileRestriction, ExecutorFailureReason.ProfileTargetMissing);
                category = code.GetString() switch { "repository_access" => ExecutorFailureCategory.RepositoryAccess, "profile_restriction" => ExecutorFailureCategory.ProfileRestriction, _ => category };
            }
        }
        catch (System.Text.Json.JsonException) { }
        return (category, ExecutorFailureReason.Unspecified);
    }

}
