using AIEngineeringPlatform.Infrastructure;
using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace AIEngineeringPlatform.Certification;

// Owner-operated harness only. It never writes target source or calls the executor's mutation API.
internal static partial class Program
{
    internal const string Owner = "ghptc";
    internal const string Repository = "aep-sandbox-m8-20261028-3a0ed9";
    internal const long Installation = 176070757;
    internal const decimal AuthorizedMaximumUsd = 10m;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static async Task<int> Main(string[] args)
    {
        try
        {
            ValidateArguments(args);
            string Option(string name, string fallback) { var i = Array.IndexOf(args, name); return i < 0 ? fallback : i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing option value."); }
            var settings = args.Contains("--observe") ? new Dictionary<string, string>() : ReadSettings(Option("--settings", ".env"));
            var output = Path.GetFullPath(Option("--output", "/tmp/aep-m5-certification"));
            Directory.CreateDirectory(output);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(output, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (args.Contains("--start")) { await StartAsync(settings, output, Option("--password-file", ""), CancellationToken.None); return 0; }
            if (args.Contains("--observe")) { await ObserveAsync(output, Option("--password-file", ""), CancellationToken.None); return 0; }
            if (!args.Contains("--preflight")) throw new ArgumentException("Select --preflight; starting or modifying a run is not an implicit action.");
            var session = Path.Combine(output, "preflight-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(session);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(session, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await PreflightAsync(settings, session, CancellationToken.None);
            Console.WriteLine("Preflight evidence: " + session);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Certification stopped: " + error.GetType().Name + ": " + SecretRedactor.Redact(error.Message));
            return 1;
        }
    }
    internal static void ValidateArguments(string[] args)
    {
        var actions = new[] { "--preflight", "--start", "--observe" };
        var values = new[] { "--settings", "--output", "--password-file" };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!seen.Add(args[i])) throw new ArgumentException("Duplicate certification option.");
            if (actions.Contains(args[i])) continue;
            if (!values.Contains(args[i]) || ++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Unknown option or missing certification value.");
        }
        if (actions.Count(seen.Contains) != 1) throw new ArgumentException("Select exactly one of --preflight, --start or --observe.");
        if ((seen.Contains("--start") || seen.Contains("--observe")) && !seen.Contains("--password-file")) throw new ArgumentException("The owner password file is required for this action.");
    }
    internal static Dictionary<string, string> ReadSettings(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            var index = line.IndexOf('='); if (index < 1) throw new ArgumentException("Invalid owner settings line.");
            var key = line[..index].Trim(); var value = line[(index + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'')) value = value[1..^1];
            values.Add(key, value); // Data only; no shell evaluation or variable expansion.
        }
        return values;
    }
    private static string Required(IReadOnlyDictionary<string, string> settings, string key) => settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ConfigurationException("Owner setting is required: " + key);
    private static async Task PreflightAsync(IReadOnlyDictionary<string, string> settings, string output, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(2)); ct = deadline.Token;
        using var handler = new InstallationPermissionCapture { InnerHandler = new HttpClientHandler { AllowAutoRedirect = false } };
        using var github = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var source = new GitHubSourceControl(github, Required(settings, "GITHUB_APP_ID"), Required(settings, "GITHUB_APP_PRIVATE_KEY_PATH"), null);
        // Reuse production App JWT/token code. Requested scope is this repository only.
        var token = await source.CreateInstallationTokenAsync(new(Owner, Repository, Installation), ct);
        var permissions = handler.Permissions ?? throw new InvalidOperationException("GitHub did not return verifiable installation permissions.");
        foreach (var expected in new Dictionary<string, string> { ["contents"] = "write", ["pull_requests"] = "write", ["metadata"] = "read" })
            if (permissions.GetValueOrDefault(expected.Key) != expected.Value) throw new UnauthorizedAccessException("Required existing installation permission is unavailable: " + expected.Key);
        using var accessible = await GetAsync(github, "https://api.github.com/installation/repositories?per_page=100", token, true, ct);
        var repositories = accessible.RootElement.GetProperty("repositories").EnumerateArray().ToArray();
        if (accessible.RootElement.GetProperty("total_count").GetInt32() != 1 || repositories.Length != 1 || repositories[0].GetProperty("full_name").GetString() != Owner + "/" + Repository) throw new UnauthorizedAccessException("Installation token is not restricted to the explicitly authorized repository.");
        var prefix = "https://api.github.com/repos/" + Owner + "/" + Repository;
        using var metadata = await GetAsync(github, prefix, token, true, ct);
        if (metadata.RootElement.GetProperty("full_name").GetString() != Owner + "/" + Repository || metadata.RootElement.GetProperty("archived").GetBoolean() || metadata.RootElement.GetProperty("disabled").GetBoolean()) throw new UnauthorizedAccessException("Repository identity or writable state cannot be verified.");
        var branch = metadata.RootElement.GetProperty("default_branch").GetString() ?? throw new InvalidOperationException("Default branch is unavailable.");
        using var head = await GetAsync(github, prefix + "/branches/" + Uri.EscapeDataString(branch), token, true, ct);
        if (branch != "main") throw new UnauthorizedAccessException("Certification authorization expects the verified main default branch.");
        var sha = head.RootElement.GetProperty("commit").GetProperty("sha").GetString()!;
        if (!System.Text.RegularExpressions.Regex.IsMatch(sha, "\\A[a-f0-9]{40}\\z")) throw new InvalidOperationException("Default source revision is invalid.");
        var treeSha = head.RootElement.GetProperty("commit").GetProperty("commit").GetProperty("tree").GetProperty("sha").GetString()!;
        using var tree = await GetAsync(github, prefix + "/git/trees/" + treeSha + "?recursive=1", token, true, ct);
        if (tree.RootElement.GetProperty("truncated").GetBoolean()) throw new InvalidOperationException("Repository inventory exceeds the preflight bound.");
        var entries = tree.RootElement.GetProperty("tree").EnumerateArray().ToArray();
        if (entries.Length > 10000) throw new InvalidOperationException("Repository inventory exceeds the preflight bound.");
        var branches = new List<JsonElement>();
        for (var page = 1; page <= 10; page++)
        {
            using var batch = await GetAsync(github, prefix + "/branches?per_page=100&page=" + page, token, true, ct);
            branches.AddRange(batch.RootElement.EnumerateArray().Select(x => JsonSerializer.SerializeToElement(new { name = x.GetProperty("name"), sha = x.GetProperty("commit").GetProperty("sha") })));
            if (batch.RootElement.GetArrayLength() < 100) break;
            if (page == 10) throw new InvalidOperationException("Branch inventory exceeds the bounded preflight.");
        }
        var evidence = new
        {
            version = 1, checkedAt = DateTimeOffset.UtcNow, repository = Owner + "/" + Repository, installationId = Installation,
            repositoryId = metadata.RootElement.GetProperty("id"), permissions, scopedRepositoryCount = 1,
            defaultBranch = branch, defaultHead = sha, defaultTree = treeSha, defaultProtected = head.RootElement.GetProperty("protected"), branches,
            files = entries.Where(x => x.GetProperty("type").GetString() == "blob").Select(x => new { path = x.GetProperty("path"), sha = x.GetProperty("sha"), size = x.TryGetProperty("size", out var size) ? size.GetInt64() : 0 }).ToArray(),
            authorizedMaximumOpenAiUsd = AuthorizedMaximumUsd, openAiSpentUsd = 0, targetSourceModified = false
        };
        await SaveAsync(Path.Combine(output, "github-preflight.json"), evidence, ct);
        Console.WriteLine($"Verified GitHub App installation {Installation}: {Owner}/{Repository}; contents/write + pull_requests/write; baseline {branch}@{sha}; existing branches={branches.Count}; files={entries.Count(x => x.GetProperty("type").GetString() == "blob")}. No target source or branch was changed.");
        // Free model-access metadata check only; no Responses call and no paid probe.
        using var openai = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        var model = Required(settings, "OPENAI_DEFAULT_MODEL");
        using var modelMetadata = await GetAsync(openai, "https://api.openai.com/v1/models/" + Uri.EscapeDataString(model), Required(settings, "OPENAI_API_KEY"), false, ct);
        if (modelMetadata.RootElement.GetProperty("id").GetString() != model) throw new InvalidOperationException("Configured model access could not be verified.");
        await SaveAsync(Path.Combine(output, "model-access.json"), new { checkedAt = DateTimeOffset.UtcNow, model, accessVerified = true, paidRequestStarted = false, source = "GET /v1/models/{model}" }, ct);
        Console.WriteLine("Verified configured Responses model access: " + model + "; no paid model request was made.");
    }
    private static async Task<JsonDocument> GetAsync(HttpClient http, string url, string token, bool github, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.Authorization = new("Bearer", token);
        if (github) { request.Headers.UserAgent.ParseAdd("AEP-Certification/1.0"); request.Headers.Accept.ParseAdd("application/vnd.github+json"); request.Headers.Add("X-GitHub-Api-Version", "2022-11-28"); }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Authorized metadata verification returned HTTP " + (int)response.StatusCode + "; no source operation or paid request was started.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct); using var buffer = new MemoryStream();
        var bytes = new byte[8192]; int length;
        while ((length = await stream.ReadAsync(bytes, ct)) > 0) { if (buffer.Length + length > 4 * 1024 * 1024) throw new InvalidOperationException("Metadata response exceeds its bound."); await buffer.WriteAsync(bytes.AsMemory(0, length), ct); }
        return JsonDocument.Parse(buffer.ToArray());
    }
    private static async Task SaveAsync(string path, object value, CancellationToken ct)
    {
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json) + "\n", ct);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    private sealed class InstallationPermissionCapture : DelegatingHandler
    {
        public Dictionary<string, string>? Permissions { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (request.Method == HttpMethod.Post && request.RequestUri?.Host == "api.github.com" && request.RequestUri.AbsolutePath == $"/app/installations/{Installation}/access_tokens" && response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                if (content.Length > 131072) throw new InvalidOperationException("Installation receipt exceeds its bound.");
                using var document = JsonDocument.Parse(content);
                Permissions = document.RootElement.GetProperty("permissions").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString()!);
            }
            return response;
        }
    }
}
