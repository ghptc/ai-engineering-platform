using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Application.Dependencies;
using AIEngineeringPlatform.Domain;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace AIEngineeringPlatform.LocalExecutor;

// Trusted control-plane acquisition only. The helper has network=none and never receives credentials.
public sealed class DependencyExecutor(ExecutorOptions options, DockerWorkload docker, ControlledProcess controlled)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    private string DirectoryFor(string id) { if (!Regex.IsMatch(id, "\\A[a-f0-9]{64}\\z")) throw new ArgumentException("Invalid dependency identity."); return Path.Combine(options.StateDirectory, "dependencies", id); }
    public async Task<DependencyResolution?> ReadAsync(string id, CancellationToken ct)
    {
        var path = Path.Combine(DirectoryFor(id), "resolution.json"); if (!File.Exists(path)) return null;
        var resolution = JsonSerializer.Deserialize<DependencyResolution>(await File.ReadAllTextAsync(path, ct), Json)!;
        if (resolution.PlanSha256 != DependencyGovernance.ResolutionFingerprint(resolution)) throw new InvalidOperationException("Dependency resolution fingerprint mismatch.");
        return resolution;
    }
    public async Task<DependencyResolution> ResolveAsync(DependencyApprovalRequest request, RepositoryConnection repository, CancellationToken caller)
    {
        DependencyGovernance.Validate(request.Proposal);
        if (request.Status != DependencyApprovalStatus.Resolving || request.Audit.LastOrDefault() is not { RegistryAccess: true } audit || audit.Action is not ("Resolve" or "RetryResolution") || request.ProjectId.Length > 64 || !Regex.IsMatch(request.RunId, "\\A[a-zA-Z0-9-]{1,64}\\z")) throw new UnauthorizedAccessException("An explicit scoped owner registry authorization is required.");
        var repo = options.Repositories.SingleOrDefault(x => x.Owner == repository.Owner && x.Name == repository.Repository && x.InstallationId == repository.InstallationId && x.DefaultBranch == repository.DefaultBranch) ?? throw new UnauthorizedAccessException("Repository is not allowlisted.");
        await gate.WaitAsync(caller);
        var directory = DirectoryFor(request.Id); var name = "aep-deps-" + request.Id[..24];
        try
        {
            var prior = await ReadAsync(request.Id, caller); if (prior != null) return prior;
            Directory.CreateDirectory(directory); File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var selectedImage = repo.Profiles.Values.First().Image;
            var imageCheck = await controlled.RunAsync("/usr/bin/docker", ["image", "inspect", "--format", "{{.Id}}", selectedImage], options.StateDirectory, 15, 10000, caller);
            if (imageCheck.ExitCode != 0 || imageCheck.Output.Trim() != selectedImage) throw new ConfigurationException("Pinned dependency runtime image is unavailable; no acquisition started.");
            var pending = Path.Combine(directory, "acquisition-started.json");
            if (File.Exists(pending) && audit.Action == "RetryResolution")
            {
                var active = await controlled.RunAsync("/usr/bin/docker", ["ps", "-aq", "--filter", "name=^/" + name + "$"], options.StateDirectory, 10, 10000, caller);
                if (active.ExitCode != 0 || active.Output.Trim().Length != 0) throw new InvalidOperationException("Prior acquisition is still active or cannot be reconciled.");
                File.Move(pending, pending + ".prior-" + request.Version);
            }
            using (var fence = new FileStream(pending, FileMode.CreateNew, FileAccess.Write)) { await JsonSerializer.SerializeAsync(fence, new { request.Id, request.RunId, request.ProjectId, repository, StartedAt = DateTimeOffset.UtcNow }, cancellationToken: caller); }
            var workspace = Path.Combine(options.StateDirectory, "runs", request.RunId, "repository");
            if (!Directory.Exists(Path.Combine(workspace, ".git"))) throw new InvalidOperationException("The existing execution workspace must be established before dependency resolution.");
            using (var workspaceState = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(options.StateDirectory, "runs", request.RunId, "state.json"), caller)))
            {
                var expectedIdentity = $"{repository.Owner}/{repository.Repository}@{repository.InstallationId}:{repository.DefaultBranch}";
                if (workspaceState.RootElement.GetProperty("repositoryIdentity").GetString() != expectedIdentity) throw new UnauthorizedAccessException("Existing workspace repository identity differs from the owner-authorized scope.");
            }
            var manifestPath = WorkspaceFiles.Resolve(workspace, "frontend/package.json"); var lockPath = WorkspaceFiles.Resolve(workspace, "frontend/package-lock.json");
            var before = File.Exists(manifestPath) ? await File.ReadAllTextAsync(manifestPath, caller) : "";
            var beforeLock = File.Exists(lockPath) ? await File.ReadAllTextAsync(lockPath, caller) : "";
            if (before.Length > 65536 || beforeLock.Length > 2_000_000) throw new ArgumentException("Existing manifest or lock exceeds bounds.");
            var manifest = before == "" ? new JsonObject { ["name"] = "aep-project", ["version"] = "0.0.0", ["private"] = true, ["dependencies"] = JsonSerializer.SerializeToNode(ApprovedFrontendBootstrap.Dependencies) } : JsonNode.Parse(before)!.AsObject();
            ValidateManifest(manifest);
            foreach (var package in request.Proposal.Packages)
            {
                var section = package.Development ? "devDependencies" : "dependencies";
                manifest[package.Development ? "dependencies" : "devDependencies"]?.AsObject().Remove(package.Name);
                manifest[section] ??= new JsonObject(); manifest[section]![package.Name] = package.Version;
            }
            var sanitized = new JsonObject { ["name"] = "aep-resolution", ["version"] = "0.0.0", ["private"] = true };
            foreach (var section in new[] { "dependencies", "devDependencies" }) if (manifest[section] != null) sanitized[section] = manifest[section]!.DeepClone();
            var acquisitionStartedAt = DateTimeOffset.UtcNow;
            var memory = Math.Min(1024, repo.Profiles.Values.Min(p => p.MemoryMb));
            var maximumSeconds = Math.Min(120, repo.Profiles.Values.Min(p => p.TimeoutSeconds));
            await docker.VerifyHostAsync(caller);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(caller); var remaining = request.ExecutionDeadline - DateTimeOffset.UtcNow;
            if (remaining == null || remaining <= TimeSpan.Zero) throw new ExecutionLimitException("Original execution deadline is unavailable or expired.");
            timeout.CancelAfter(remaining.Value < TimeSpan.FromSeconds(maximumSeconds) ? remaining.Value : TimeSpan.FromSeconds(maximumSeconds)); var ct = timeout.Token;
            string[] createArguments = ["create", "--pull", "never", "--name", name, "--label", "aep.executor=" + docker.OwnershipLabel, "--network", "none", "--ipc", "private", "--cgroupns", "private", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges=true", "--user", "65532:65532", "--pids-limit", "128", "--cpus", "1", "--memory", memory + "m", "--memory-swap", memory + "m", "--ulimit", "nofile=1024:1024", "--tmpfs", "/workspace:rw,exec,nosuid,nodev,size=256m,uid=65532,gid=65532,mode=0700", "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=256m,uid=65532,gid=65532,mode=0700", "--log-driver", "none", "--interactive", "--entrypoint", "python3", repo.Profiles.Values.First().Image, "/opt/aep/dependencies.py"];
            var created = await controlled.RunAsync("/usr/bin/docker", createArguments, options.StateDirectory, 15, 10000, ct);
            if (created.ExitCode != 0) throw new InvalidOperationException("Cannot create isolated dependency helper.");
            var inspected = await controlled.RunAsync("/usr/bin/docker", ["inspect", name], options.StateDirectory, 15, 100000, ct);
            if (inspected.ExitCode != 0) throw new InvalidOperationException("Cannot inspect dependency helper isolation.");
            DockerWorkload.VerifyContainer(inspected.Output, selectedImage, memory);
            var start = new ProcessStartInfo("/usr/bin/docker") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in new[] { "start", "--attach", "--interactive", name }) start.ArgumentList.Add(arg);
            start.Environment.Clear(); start.Environment["PATH"] = "/usr/bin:/bin"; start.Environment["DOCKER_HOST"] = options.DockerHost;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start dependency helper.");
            var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, ct);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { manifest = sanitized, @lock = "" }).AsMemory(), ct); await process.StandardInput.FlushAsync(ct);
            using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(20) };
            long totalBytes = 0; var calls = 0; JsonElement? result = null;
            while (true)
            {
                var line = await ReadLineAsync(process.StandardOutput, 72_000_000, ct); if (line == null) break;
                using var parsed = JsonDocument.Parse(line); var message = parsed.RootElement;
                if (message.GetProperty("kind").GetString() == "registry")
                {
                    var path = message.GetProperty("path").GetString()!;
                    var uri = RegistryUri(path);
                    if (++calls > 1000) throw new InvalidOperationException("Registry request limit exceeded.");
                    using var query = new HttpRequestMessage(HttpMethod.Get, uri); query.Headers.Accept.ParseAdd("application/vnd.npm.install-v1+json");
                    using var response = await http.SendAsync(query, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Registry lookup rejected (HTTP " + (int)response.StatusCode + ").");
                    var body = await ReadBoundedAsync(await response.Content.ReadAsStreamAsync(ct), path.Contains("/-/", StringComparison.Ordinal) ? 20_000_000 : 8_000_000, ct);
                    totalBytes += body.Length; if (totalBytes > 64_000_000) throw new InvalidOperationException("Registry acquisition byte limit exceeded.");
                    try { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { status = 200, body = Convert.ToBase64String(body) }).AsMemory(), ct); await process.StandardInput.FlushAsync(ct); }
                    catch (IOException)
                    {
                        var diagnosticLine = await ReadLineAsync(process.StandardOutput, 8000, ct);
                        throw new InvalidOperationException("Dependency helper closed its protocol before consuming the response: " + (diagnosticLine ?? "no final protocol message"));
                    }
                }
                else if (message.GetProperty("kind").GetString() == "result") { result = message.Clone(); break; }
                else throw new InvalidOperationException("Dependency helper failed before source changes: " + (message.TryGetProperty("message", out var diagnostic) ? diagnostic.GetString() : "No diagnostic"));
            }
            if (result == null) throw new InvalidOperationException("Dependency helper produced no resolution receipt.");
            process.StandardInput.Close(); await process.WaitForExitAsync(ct); await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException("Dependency helper did not complete successfully.");
            var lockText = result.Value.GetProperty("lock").GetString()!; if (lockText.Length > 2_000_000) throw new InvalidOperationException("Resolved lock exceeds bounds.");
            var lockJson = JsonNode.Parse(lockText)!.AsObject(); lockJson["name"] = manifest["name"]?.DeepClone(); lockJson["version"] = manifest["version"]?.DeepClone();
            var root = lockJson["packages"]![""]!.AsObject(); root["name"] = manifest["name"]?.DeepClone(); root["version"] = manifest["version"]?.DeepClone();
            lockText = lockJson.ToJsonString() + "\n";
            var packages = Packages(lockJson, request.Proposal);
            var cache = Convert.FromBase64String(result.Value.GetProperty("cache").GetString()!); if (cache.Length > 52_000_000) throw new InvalidOperationException("Resolved cache exceeds bounds.");
            DependencyPreparation.ValidateCache(cache, packages);
            var cacheSha = Hash(cache); if (cacheSha != result.Value.GetProperty("cacheSha256").GetString()) throw new InvalidOperationException("Cache integrity mismatch.");
            var manifestText = manifest.ToJsonString(new() { WriteIndented = true }) + "\n";
            var planSha = DependencyGovernance.Hash(JsonSerializer.Serialize(new { request.Id, request.ProjectId, request.RunId, repository, manifestText, lockText, cacheSha, ResolverImage = selectedImage }));
            var resolution = new DependencyResolution(request.Id, request.ProjectId, request.RunId, repository, Hash(Encoding.UTF8.GetBytes(before)), Hash(Encoding.UTF8.GetBytes(beforeLock)), manifestText, lockText, cacheSha, packages, planSha, ResolverImage: selectedImage, Acquisition: new(calls, totalBytes, acquisitionStartedAt, DateTimeOffset.UtcNow, process.ExitCode));
            resolution = resolution with { PlanSha256 = DependencyGovernance.ResolutionFingerprint(resolution) };
            DependencyGovernance.ValidateResolution(request, repository, resolution);
            await File.WriteAllBytesAsync(Path.Combine(directory, "cache.tar"), cache, ct);
            await AtomicJsonAsync(Path.Combine(directory, "resolution.json"), resolution, ct);
            return resolution;
        }
        finally
        {
            try
            {
                await controlled.RunAsync("/usr/bin/docker", ["rm", "--force", "--volumes", name], options.StateDirectory, 15, 10000, CancellationToken.None);
                var inspection = await controlled.RunAsync("/usr/bin/docker", ["ps", "-aq", "--filter", "name=^/" + name + "$"], options.StateDirectory, 10, 10000, CancellationToken.None);
                if (inspection.ExitCode != 0 || inspection.Output.Trim().Length != 0) throw new InvalidOperationException("Dependency helper cleanup could not be verified; executor is blocked until owner reconciliation.");
            }
            catch { docker.MarkCleanupFaulted(); throw; }
            finally { gate.Release(); }
        }
    }
    public async Task GrantAsync(DependencyGrant grant, CancellationToken ct)
    {
        var resolution = await ReadAsync(grant.Id, ct) ?? throw new KeyNotFoundException("Resolution receipt missing.");
        if (grant.ProjectId != resolution.ProjectId || grant.RunId != resolution.RunId || grant.Repository != resolution.Repository || grant.PlanSha256 != resolution.PlanSha256 || string.IsNullOrWhiteSpace(grant.Owner) || !resolution.Packages.Select(x => x.Name + "@" + x.Version).Distinct().Order().SequenceEqual(grant.PackageIds.Distinct().Order())) throw new UnauthorizedAccessException("Grant cannot broaden or omit the resolved graph.");
        var grantPath = Path.Combine(DirectoryFor(grant.Id), "grant.json");
        if (File.Exists(grantPath))
        {
            var existing = JsonSerializer.Deserialize<DependencyGrant>(await File.ReadAllTextAsync(grantPath, ct), Json)!;
            if (existing.Owner != grant.Owner || existing.PlanSha256 != grant.PlanSha256) throw new UnauthorizedAccessException("An existing grant cannot be rewritten.");
            return;
        }
        await AtomicJsonAsync(grantPath, grant, ct);
    }
    public static Uri RegistryUri(string path)
    {
        var decoded = Uri.UnescapeDataString(path);
        if (path.Length > 500 || decoded.Contains("..", StringComparison.Ordinal) || decoded.Contains('\\') || decoded.Contains('?') || decoded.Contains('#') || decoded.Contains(':') || !Regex.IsMatch(decoded, "\\A/(?:@[a-z0-9._-]+/)?[a-z0-9._-]+(?:/-/[a-zA-Z0-9._-]+\\.tgz)?\\z")) throw new UnauthorizedAccessException("Only official npm package metadata/tarballs may be requested.");
        return new Uri("https://registry.npmjs.org" + path);
    }
    public static void ValidateManifest(JsonObject manifest)
    {
        if (manifest.Any(x => x.Key is not ("name" or "version" or "private" or "type" or "description" or "scripts" or "dependencies" or "devDependencies"))) throw new ArgumentException("Unsupported manifest fields; workspaces, overrides, registry configuration and optional dependency injection are not supported.");
        foreach (var section in new[] { "dependencies", "devDependencies" })
        {
            if (manifest[section] is not null && manifest[section] is not JsonObject) throw new ArgumentException("Manifest dependency sections must be objects.");
            if (manifest[section] is JsonObject dependencies)
                foreach (var package in dependencies)
                    if (!DependencyGovernance.PackageName().IsMatch(package.Key) || !DependencyGovernance.ExactVersion().IsMatch(package.Value?.GetValue<string>() ?? "")) throw new ArgumentException("Manifest dependencies require exact registry versions; URLs, aliases and ranges are forbidden.");
        }
    }
    private static ResolvedDependency[] Packages(JsonObject output, DependencyProposal proposal)
    {
        var packages = output["packages"]!.AsObject().Where(x => x.Key != "").Select(x =>
        {
            var p = x.Value!.AsObject(); var installName = x.Key.Split("node_modules/", StringSplitOptions.None)[^1];
            var name = p["name"]?.GetValue<string>() ?? installName;
            var uri = new Uri(p["resolved"]!.GetValue<string>());
            if (uri.Scheme != "https" || uri.Host != "registry.npmjs.org" || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo)) throw new UnauthorizedAccessException("Lock contains an unsupported source.");
            if (!Uri.UnescapeDataString(uri.AbsolutePath).StartsWith("/" + name + "/-/", StringComparison.Ordinal)) throw new UnauthorizedAccessException("Package identity differs from its registry tarball origin.");
            var isDirect = x.Key == "node_modules/" + name && (output["packages"]![""]!["dependencies"]?[name] != null || output["packages"]![""]!["devDependencies"]?[name] != null);
            return new ResolvedDependency(name, p["version"]!.GetValue<string>(), p["integrity"]!.GetValue<string>(), "https://registry.npmjs.org", isDirect, isDirect ? output["packages"]![""]!["devDependencies"]?[name] != null : p["dev"]?.GetValue<bool>() ?? false, p["license"]?.ToJsonString() ?? "Unknown", "Unknown — no vulnerability scan certification", p["hasInstallScript"]?.GetValue<bool>() ?? false, x.Key, installName == name ? null : installName);
        }).ToArray();
        if (packages.GroupBy(x => x.Name + "@" + x.Version).Any(x => x.Select(p => p.Integrity).Distinct().Count() != 1)) throw new UnauthorizedAccessException("One package identity resolved to inconsistent integrity values.");
        return packages;
    }
    internal static async Task AtomicJsonAsync<T>(string path, T data, CancellationToken ct) { var temp = path + ".tmp-" + Guid.NewGuid().ToString("N"); await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(data), ct); File.Move(temp, path, true); }
    internal static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static async Task<byte[]> ReadBoundedAsync(Stream input, int maximum, CancellationToken ct) { await using var stream = input; using var output = new MemoryStream(); var bytes = new byte[8192]; int n; while ((n = await stream.ReadAsync(bytes, ct)) > 0) { if (output.Length + n > maximum) throw new InvalidOperationException("Registry response exceeds byte bound."); output.Write(bytes, 0, n); } return output.ToArray(); }
    private static async Task<string?> ReadLineAsync(StreamReader reader, int maximum, CancellationToken ct)
    {
        // Only the fixed resource-bounded helper writes stdout; npm output is redirected to its private log.
        var line = await reader.ReadLineAsync(ct); if (line?.Length > maximum) throw new InvalidOperationException("Helper protocol exceeds bound."); return line;
    }
}
