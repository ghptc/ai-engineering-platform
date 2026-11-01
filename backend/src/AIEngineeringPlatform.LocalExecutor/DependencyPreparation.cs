using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace AIEngineeringPlatform.LocalExecutor;
public static class DependencyPreparation
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public static void ValidateCache(byte[] bytes, ResolvedDependency[] packages)
    {
        using var input = new MemoryStream(bytes); using var reader = new TarReader(input); TarEntry? entry; var hashes = new HashSet<string>(); long size = 0; var count = 0;
        while ((entry = reader.GetNextEntry()) != null)
        {
            if (++count > 4000 || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || !Regex.IsMatch(entry.Name, "\\Acontent-v2/sha512/[a-f0-9]{2}/[a-f0-9]{2}/[a-f0-9]{124}\\z") || entry.Length > 20_000_000 || (size += entry.Length) > 48_000_000) throw new InvalidOperationException("Unsafe dependency cache archive.");
            var expected = entry.Name["content-v2/sha512/".Length..].Replace("/", "", StringComparison.Ordinal);
            var actual = Convert.ToHexString(SHA512.HashData(entry.DataStream!)).ToLowerInvariant();
            if (actual != expected || !hashes.Add(actual)) throw new InvalidOperationException("Dependency cache content integrity mismatch.");
        }
        foreach (var package in packages)
            if (!hashes.Contains(Convert.ToHexString(Convert.FromBase64String(package.Integrity["sha512-".Length..])).ToLowerInvariant())) throw new InvalidOperationException("Package tarball integrity is absent from the verified cache.");
    }
    public static async Task<string> PrepareAsync(ExecutorOptions options, SandboxRequest request, string workspace, CancellationToken ct)
    {
        if (request.Arguments.EnumerateObject().Any(x => x.Name != "requestId") || !request.Arguments.TryGetProperty("requestId", out var value) || !Regex.IsMatch(value.GetString() ?? "", "\\A[a-f0-9]{64}\\z")) throw new ArgumentException("PrepareApprovedDependencies requires an exact requestId only.");
        var id = value.GetString()!; var (resolution, cache) = await LoadAsync(options, request, id, ct);
        var manifest = WorkspaceFiles.Resolve(workspace, "frontend/package.json"); var locked = WorkspaceFiles.Resolve(workspace, "frontend/package-lock.json");
        void Reconcile(string path, string before, string after)
        {
            var actual = DependencyExecutor.Hash(File.Exists(path) ? File.ReadAllBytes(path) : []);
            if (actual != before && actual != DependencyExecutor.Hash(Encoding.UTF8.GetBytes(after))) throw new InvalidOperationException("Manifest/lock changed since owner resolution. Preserve source and obtain a revised dependency plan.");
        }
        Reconcile(manifest, resolution.ManifestBeforeSha256, resolution.Manifest); Reconcile(locked, resolution.LockBeforeSha256, resolution.Lock);
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        // Replays reconcile each file to before/after bytes; intervening edits are never overwritten.
        foreach (var (path, content) in new[] { (manifest, resolution.Manifest), (locked, resolution.Lock) })
        {
            var temp = path + ".aep-" + request.OperationId; await File.WriteAllTextAsync(temp, content, ct); File.Move(temp, path, true);
        }
        var pointer = Path.Combine(options.StateDirectory, "runs", request.RunId, "dependency-grant.json");
        await DependencyExecutor.AtomicJsonAsync(pointer, new { Id = id, request.ProjectId, request.RunId, resolution.PlanSha256 }, ct);
        return JsonSerializer.Serialize(new { RequestId = id, resolution.PlanSha256, Files = new[] { "frontend/package.json", "frontend/package-lock.json" }, CacheSha256 = DependencyExecutor.Hash(cache), NetworkEnabled = false, InstallScriptsEnabled = false });
    }
    private static async Task<(DependencyResolution Plan, byte[] Cache)> LoadAsync(ExecutorOptions options, SandboxRequest request, string id, CancellationToken ct)
    {
        var directory = Path.Combine(options.StateDirectory, "dependencies", id);
        var grantPath = Path.Combine(directory, "grant.json");
        if (!File.Exists(grantPath)) throw new UnauthorizedAccessException("This dependency graph has no owner grant.");
        var grant = JsonSerializer.Deserialize<DependencyGrant>(await File.ReadAllTextAsync(grantPath, ct), Json)!;
        var plan = JsonSerializer.Deserialize<DependencyResolution>(await File.ReadAllTextAsync(Path.Combine(directory, "resolution.json"), ct), Json)!;
        if (plan.PlanSha256 != AIEngineeringPlatform.Application.Dependencies.DependencyGovernance.ResolutionFingerprint(plan) || !grant.PackageIds.Distinct().Order().SequenceEqual(plan.Packages.Select(x => x.Name + "@" + x.Version).Distinct().Order())) throw new UnauthorizedAccessException("Dependency grant/plan fingerprint is invalid.");
        if (grant.Id != id || grant.ProjectId != request.ProjectId || grant.RunId != request.RunId || grant.Repository != request.Repository || grant.PlanSha256 != plan.PlanSha256 || plan.Id != id || plan.RunId != request.RunId || plan.ProjectId != request.ProjectId || plan.Repository != request.Repository) throw new UnauthorizedAccessException("Dependency grants are scoped to the exact project, run and repository.");
        var cachePath = Path.Combine(directory, "cache.tar");
        if (new FileInfo(cachePath).Length > 52_000_000) throw new InvalidOperationException("Dependency cache file exceeds retention bounds.");
        var cache = await File.ReadAllBytesAsync(cachePath, ct);
        if (DependencyExecutor.Hash(cache) != plan.CacheSha256) throw new InvalidOperationException("Dependency cache receipt mismatch.");
        ValidateCache(cache, plan.Packages); return (plan, cache);
    }
    public static async Task<byte[]?> CacheForProfileAsync(ExecutorOptions options, SandboxRequest request, string workspace, CancellationToken ct)
    {
        if (request.Operation == "RunTests" && request.Arguments.TryGetProperty("profile", out var requestedProfile) && requestedProfile.GetString() == "backend") return null;
        if (File.Exists(WorkspaceFiles.Resolve(workspace, "frontend/npm-shrinkwrap.json"))) throw new ExecutorFailureException(ExecutorFailureCategory.ProfileRestriction, request.OperationId, request.Operation, 422, false, ExecutorFailureReason.DependencyApprovalRequired);
        var pointer = Path.Combine(options.StateDirectory, "runs", request.RunId, "dependency-grant.json");
        if (!File.Exists(pointer))
        {
            var source = WorkspaceFiles.Resolve(workspace, "frontend/package.json");
            if (File.Exists(source))
            {
                if (new FileInfo(source).Length > 65536) throw new ArgumentException("Frontend manifest exceeds source preparation bounds.");
                var legacyManifest = JsonNode.Parse(await File.ReadAllTextAsync(source, ct))!.AsObject();
                DependencyExecutor.ValidateManifest(legacyManifest);
                foreach (var section in new[] { "dependencies", "devDependencies" })
                    if (legacyManifest[section] is JsonObject values)
                        foreach (var package in values)
                            if (!ApprovedFrontendBootstrap.Dependencies.TryGetValue(package.Key, out var version) || package.Value?.GetValue<string>() != version)
                                throw new ExecutorFailureException(ExecutorFailureCategory.ProfileRestriction, request.OperationId, request.Operation, 422, false, ExecutorFailureReason.DependencyApprovalRequired);
            }
            var lockPath = WorkspaceFiles.Resolve(workspace, "frontend/package-lock.json");
            if (File.Exists(lockPath) && (new FileInfo(lockPath).Length > 2_000_000 || !ApprovedFrontendBootstrap.IsCatalogLock(await File.ReadAllTextAsync(lockPath, ct)))) throw new ExecutorFailureException(ExecutorFailureCategory.ProfileRestriction, request.OperationId, request.Operation, 422, false, ExecutorFailureReason.DependencyApprovalRequired);
            return null;
        }
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(pointer, ct)); var id = document.RootElement.GetProperty("Id").GetString()!;
        var (plan, cache) = await LoadAsync(options, request, id, ct);
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(WorkspaceFiles.Resolve(workspace, "frontend/package.json"), ct))!.AsObject(); DependencyExecutor.ValidateManifest(manifest);
        var expected = JsonNode.Parse(plan.Manifest)!.AsObject();
        foreach (var section in new[] { "dependencies", "devDependencies" })
            if (!JsonNode.DeepEquals(manifest[section], expected[section])) throw new UnauthorizedAccessException("Source dependencies differ from the owner-approved graph.");
        if (DependencyExecutor.Hash(await File.ReadAllBytesAsync(WorkspaceFiles.Resolve(workspace, "frontend/package-lock.json"), ct)) != DependencyExecutor.Hash(Encoding.UTF8.GetBytes(plan.Lock))) throw new UnauthorizedAccessException("Lockfile differs from the owner-approved resolution receipt.");
        return cache;
    }
}
