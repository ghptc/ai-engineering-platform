using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AIEngineeringPlatform.LocalExecutor;

// Trusted catalog data, never a repository-selected command or dependency resolver.
public static class ApprovedFrontendBootstrap
{
    private static readonly Lazy<string> Catalog = new(() =>
    {
        using var stream = typeof(ApprovedFrontendBootstrap).Assembly.GetManifestResourceStream("AEP.ApprovedFrontendLock") ?? throw new InvalidOperationException("Approved dependency catalog is unavailable.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    });
    public static string CatalogHash => Hash(Catalog.Value);
    public static IReadOnlyDictionary<string, string> Dependencies => JsonNode.Parse(Catalog.Value)!["packages"]![""]!["dependencies"]!.AsObject().ToDictionary(x => x.Key, x => x.Value!.GetValue<string>(), StringComparer.Ordinal);
    public static object Capability => new { Operation = "PrepareFrontendLock", Path = "frontend/package-lock.json", CatalogSha256 = CatalogHash, ApprovedDependencies = Dependencies, AllowedManifestFields = new[] { "name", "version", "private", "type", "scripts", "dependencies", "devDependencies", "description" }, Policy = "Use exactly these pinned direct dependencies across dependencies/devDependencies. No overrides, optional dependencies, workspaces or registry selection. Only a missing lockfile can be created. Availability must still be verified in the configured isolated image." };
    public static string Prepare(string workspace)
    {
        var manifestPath = WorkspaceFiles.Resolve(workspace, "frontend/package.json");
        var lockPath = WorkspaceFiles.Resolve(workspace, "frontend/package-lock.json");
        if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 65536) throw new ArgumentException("Create a bounded frontend/package.json using the advertised approved dependency catalog first.");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath), new JsonDocumentOptions { MaxDepth = 12 });
        var manifest = document.RootElement;
        var allowed = new HashSet<string>(["name", "version", "private", "type", "scripts", "dependencies", "devDependencies", "description"], StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (manifest.ValueKind != JsonValueKind.Object || manifest.EnumerateObject().Any(x => !allowed.Contains(x.Name) || !seen.Add(x.Name))) throw new ArgumentException("Manifest contains unsupported or duplicate fields; preserve existing lockfiles and use the approved bootstrap shape.");
        if (!manifest.TryGetProperty("name", out var nameValue) || nameValue.ValueKind != JsonValueKind.String || !manifest.TryGetProperty("version", out var versionValue) || versionValue.ValueKind != JsonValueKind.String) throw new ArgumentException("Manifest requires a package name and exact version.");
        var name = nameValue.GetString() ?? "";
        var version = versionValue.GetString() ?? "";
        if (!Regex.IsMatch(name, @"\A[a-z0-9][a-z0-9._-]{0,99}\z") || !Regex.IsMatch(version, @"\A[0-9]+\.[0-9]+\.[0-9]+(?:-[a-zA-Z0-9.-]+)?\z") || version.Length > 80) throw new ArgumentException("Use a bounded npm package name and exact semantic version.");
        var dependencies = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var section in new[] { "dependencies", "devDependencies" })
        {
            if (!manifest.TryGetProperty(section, out var values)) continue;
            if (values.ValueKind != JsonValueKind.Object) throw new ArgumentException("Dependency sections must be exact-version objects.");
            foreach (var item in values.EnumerateObject())
                if (item.Value.ValueKind != JsonValueKind.String || !dependencies.TryAdd(item.Name, item.Value.GetString()!)) throw new ArgumentException("Dependency names must be unique across sections.");
        }
        var approved = Dependencies;
        if (dependencies.Count != approved.Count || dependencies.Any(x => !approved.TryGetValue(x.Key, out var value) || value != x.Value)) throw new ArgumentException("Dependencies must match the complete advertised exact-version catalog; arbitrary URLs, ranges and packages are not approved.");
        var output = JsonNode.Parse(Catalog.Value)!.AsObject();
        output["name"] = name; output["version"] = version;
        var root = output["packages"]![""]!.AsObject(); root["name"] = name; root["version"] = version;
        root.Remove("dependencies"); root.Remove("devDependencies");
        foreach (var section in new[] { "dependencies", "devDependencies" })
            if (manifest.TryGetProperty(section, out var values)) root[section] = JsonNode.Parse(values.GetRawText());
        var content = output.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) + "\n";
        if (Encoding.UTF8.GetByteCount(content) > 100000) throw new InvalidOperationException("Approved lock exceeds controlled source-write bounds.");
        if (File.Exists(lockPath))
        {
            // Only receipt replay may repeat a completed mutation. Never overwrite existing source.
            throw new ArgumentException("frontend/package-lock.json already exists; it was preserved. Reuse its evidence or inspect it instead of repeating bootstrap.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        // CreateNew is atomic and cannot overwrite a file that appeared after validation.
        using (var file = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var bytes = Encoding.UTF8.GetBytes(content); file.Write(bytes); file.Flush(flushToDisk: true);
        }
        return JsonSerializer.Serialize(new { Path = "frontend/package-lock.json", Sha256 = Hash(content), CatalogSha256 = CatalogHash, Created = true });
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
