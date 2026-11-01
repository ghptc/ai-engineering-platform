using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Application.Dependencies;
using AIEngineeringPlatform.Domain;
using System.Net.Http.Json;
using System.Text.Json;
namespace AIEngineeringPlatform.Infrastructure;
internal sealed class DependencyControlClient(HttpClient http, string endpoint, string token) : IDependencyExecutor
{
    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, new Uri(new Uri(endpoint), path)); message.Headers.Authorization = new("Bearer", token);
        if (body != null) message.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return default;
        if (!response.IsSuccessStatusCode) throw new ConfigurationException("Dependency control operation returned HTTP " + (int)response.StatusCode + "; reconcile its persisted outcome before retrying.");
        await using var input = await response.Content.ReadAsStreamAsync(ct); using var bytes = new MemoryStream(); var buffer = new byte[8192]; int n;
        while ((n = await input.ReadAsync(buffer, ct)) > 0) { if (bytes.Length + n > 5_000_000) throw new ConfigurationException("Dependency control response exceeded bound."); bytes.Write(buffer, 0, n); }
        return bytes.Length == 0 ? default : JsonSerializer.Deserialize<T>(bytes.ToArray(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    public async Task<DependencyResolution> ResolveAsync(DependencyApprovalRequest request, RepositoryConnection repository, CancellationToken ct) => await SendAsync<DependencyResolution>(HttpMethod.Post, "v1/dependencies/resolve", new { request, repository }, ct) ?? throw new ConfigurationException("Dependency resolution receipt is unavailable.");
    public Task<DependencyResolution?> ResolutionAsync(string id, CancellationToken ct) => SendAsync<DependencyResolution>(HttpMethod.Get, "v1/dependencies/" + Uri.EscapeDataString(id), null, ct);
    public async Task GrantAsync(DependencyGrant grant, CancellationToken ct) => await SendAsync<JsonElement>(HttpMethod.Post, "v1/dependencies/grant", grant, ct);
}
public sealed class UnsupportedDependencyExecutor : IDependencyExecutor
{
    public Task<DependencyResolution> ResolveAsync(DependencyApprovalRequest request, RepositoryConnection repository, CancellationToken ct) => throw new ConfigurationException("The configured executor does not implement dependency control v1.");
    public Task<DependencyResolution?> ResolutionAsync(string id, CancellationToken ct) => throw new ConfigurationException("The configured executor does not implement dependency control v1.");
    public Task GrantAsync(DependencyGrant grant, CancellationToken ct) => throw new ConfigurationException("The configured executor does not implement dependency control v1.");
}
