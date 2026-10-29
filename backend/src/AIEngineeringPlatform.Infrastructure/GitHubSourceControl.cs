using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace AIEngineeringPlatform.Infrastructure;

public sealed class GitHubOperationException(int status, string requestId, string providerMessage = "") : InvalidOperationException($"GitHub returned {status}. Check App permissions, installation access and repository review rules. Request id: {requestId}. {providerMessage}") { public int Status { get; } = status; }
public sealed class GitHubSourceControl(HttpClient http, string? appId, string? keyPath, string? webhookSecret) : ISourceControl
{
    public bool Configured => !string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath);
    public bool VerifyWebhook(ReadOnlySpan<byte> payload, string signature)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret) || !signature.StartsWith("sha256=", StringComparison.Ordinal)) return false;
        try { return CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(Encoding.UTF8.GetBytes(webhookSecret), payload), Convert.FromHexString(signature[7..])); } catch (FormatException) { return false; }
    }
    public static void Validate(RepositoryConnection repository)
    {
        if (!Regex.IsMatch(repository.Owner, @"\A[a-zA-Z0-9][a-zA-Z0-9-]{0,38}\z") || !Regex.IsMatch(repository.Repository, @"\A[a-zA-Z0-9_.-]{1,100}\z") || repository.Repository is "." or ".." || repository.InstallationId <= 0) throw new ArgumentException("Invalid GitHub repository identity.");
    }
    private static string Base64(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public async Task<string> CreateInstallationTokenAsync(RepositoryConnection repository, CancellationToken ct, bool contentsOnly = false)
    {
        if (!Configured) throw new ConfigurationException("GitHub App credentials are not configured.");
        using var rsa = RSA.Create(); rsa.ImportFromPem(await File.ReadAllTextAsync(keyPath!, ct));
        var now = DateTimeOffset.UtcNow;
        var input = Base64(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}")) + "." + Base64(JsonSerializer.SerializeToUtf8Bytes(new { iat = now.AddSeconds(-60).ToUnixTimeSeconds(), exp = now.AddMinutes(9).ToUnixTimeSeconds(), iss = appId }));
        var jwt = input + "." + Base64(rsa.SignData(Encoding.UTF8.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var permissions = new Dictionary<string, string> { ["contents"] = "write", ["metadata"] = "read" };
        if (!contentsOnly) permissions["pull_requests"] = "write";
        using var response = await SendAsync(HttpMethod.Post, $"app/installations/{repository.InstallationId}/access_tokens", jwt, new { repositories = new[] { repository.Repository }, permissions }, ct);
        return response.RootElement.GetProperty("token").GetString()!;
    }
    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string token, object? body, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, "https://api.github.com/" + path);
        message.Headers.Authorization = new("Bearer", token); message.Headers.UserAgent.ParseAdd("AIEngineeringPlatform/1.0");
        message.Headers.Accept.ParseAdd("application/vnd.github+json"); message.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (body != null) message.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            var providerMessage = "";
            try { using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); providerMessage = SecretRedactor.Redact(error.RootElement.GetProperty("message").GetString() ?? ""); if (providerMessage.Length > 1000) providerMessage = providerMessage[..1000]; } catch (Exception exception) when (exception is JsonException or KeyNotFoundException) { }
            throw new GitHubOperationException((int)response.StatusCode, string.Join(',', response.Headers.TryGetValues("X-GitHub-Request-Id", out var values) ? values : []), providerMessage);
        }
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
    public async Task<string> ExecuteAsync(RepositoryConnection repository, string operation, JsonElement args, string operationId, CancellationToken ct)
    {
        Validate(repository);
        var token = await CreateInstallationTokenAsync(repository, ct);
        var prefix = $"repos/{repository.Owner}/{repository.Repository}";
        string Get(string name) => args.TryGetProperty(name, out var value) ? value.GetString() ?? "" : throw new ArgumentException($"Missing {name}.");
        using var metadata = await SendAsync(HttpMethod.Get, prefix, token, null, ct);
        if (!string.Equals(metadata.RootElement.GetProperty("full_name").GetString(), repository.Owner + "/" + repository.Repository, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("GitHub repository identity mismatch.");
        JsonDocument result;
        switch (operation)
        {
            case "ReadRepository": return metadata.RootElement.GetRawText();
            case "ReconcileRepository":
                var candidates = new List<JsonElement>();
                var branches = new List<JsonElement>();
                for (var branchPage = 1; branchPage <= 10; branchPage++)
                {
                    using var batch = await SendAsync(HttpMethod.Get, prefix + $"/branches?per_page=100&page={branchPage}", token, null, ct);
                    branches.AddRange(batch.RootElement.EnumerateArray().Where(x => (x.GetProperty("name").GetString() ?? "").StartsWith("engineering/", StringComparison.Ordinal)).Select(x => JsonSerializer.SerializeToElement(new { branch = x.GetProperty("name"), commit = x.GetProperty("commit").GetProperty("sha") })));
                    if (batch.RootElement.GetArrayLength() < 100) break;
                    if (branchPage == 10) throw new InvalidOperationException("Branch inventory exceeds reconciliation bounds.");
                }
                for (var page = 1; page <= 10; page++)
                {
                    using var batch = await SendAsync(HttpMethod.Get, prefix + $"/pulls?state=all&per_page=100&page={page}", token, null, ct);
                    foreach (var item in batch.RootElement.EnumerateArray())
                        if (item.GetProperty("base").GetProperty("ref").GetString() == repository.DefaultBranch && item.GetProperty("head").GetProperty("repo").ValueKind != JsonValueKind.Null && string.Equals(item.GetProperty("head").GetProperty("repo").GetProperty("full_name").GetString(), repository.Owner + "/" + repository.Repository, StringComparison.OrdinalIgnoreCase))
                            candidates.Add(JsonSerializer.SerializeToElement(new { number = item.GetProperty("number"), url = item.GetProperty("html_url"), html_url = item.GetProperty("html_url"), mergeCommit = item.GetProperty("merge_commit_sha"), title = item.GetProperty("title"), state = item.GetProperty("state"), merged = item.GetProperty("merged_at"), branch = item.GetProperty("head").GetProperty("ref"), commit = item.GetProperty("head").GetProperty("sha"), @base = item.GetProperty("base").GetProperty("ref") }));
                    if (batch.RootElement.GetArrayLength() < 100) return JsonSerializer.Serialize(new { defaultBranch = repository.DefaultBranch, branches, pullRequests = candidates });
                }
                throw new InvalidOperationException("Repository reconciliation exceeds bounded PR inventory; narrow the task manually.");
            case "CreateBranch":
                var branch = Get("branch");
                if (!Regex.IsMatch(branch, @"\Aengineering/[a-zA-Z0-9-]{1,100}\z") || branch == repository.DefaultBranch) throw new ArgumentException("Work branches must start with engineering/.");
                try { using var existingBranch = await SendAsync(HttpMethod.Get, prefix + "/git/ref/heads/" + Uri.EscapeDataString(branch), token, null, ct); return existingBranch.RootElement.GetRawText(); } catch (GitHubOperationException exception) when (exception.Status == 404) { }
                using (var head = await SendAsync(HttpMethod.Get, prefix + "/git/ref/heads/" + Uri.EscapeDataString(repository.DefaultBranch), token, null, ct))
                    result = await SendAsync(HttpMethod.Post, prefix + "/git/refs", token, new { @ref = "refs/heads/" + branch, sha = head.RootElement.GetProperty("object").GetProperty("sha").GetString() }, ct);
                break;
            case "CreatePullRequest":
                var headBranch = Get("branch");
                if (!Regex.IsMatch(headBranch, @"\Aengineering/[a-zA-Z0-9-]{1,100}\z")) throw new ArgumentException("Invalid work branch.");
                using (var existing = await SendAsync(HttpMethod.Get, prefix + "/pulls?state=all&head=" + Uri.EscapeDataString(repository.Owner + ":" + headBranch), token, null, ct))
                    if (existing.RootElement.GetArrayLength() > 0) return existing.RootElement[0].GetRawText();
                result = await SendAsync(HttpMethod.Post, prefix + "/pulls", token, new { title = Get("title"), body = Get("body") + "\n\nOperation: " + operationId, head = headBranch, @base = repository.DefaultBranch, draft = true }, ct);
                break;
            case "ReadPullRequest":
            case "SubmitCodeReview":
            case "PublishComment":
                if (!args.TryGetProperty("number", out var numberValue) || !numberValue.TryGetInt32(out var number) || number <= 0) throw new ArgumentException("A real pull request number is required.");
                using (var pr = await SendAsync(HttpMethod.Get, prefix + $"/pulls/{number}", token, null, ct))
                {
                    if (operation == "ReadPullRequest")
                    {
                        var files = new List<JsonElement>();
                        for (var page = 1; page <= 30; page++)
                        {
                            using var batch = await SendAsync(HttpMethod.Get, prefix + $"/pulls/{number}/files?per_page=100&page={page}", token, null, ct);
                            files.AddRange(batch.RootElement.EnumerateArray().Select(x => x.Clone()));
                            if (batch.RootElement.GetArrayLength() < 100) break;
                        }
                        if (pr.RootElement.GetProperty("changed_files").GetInt32() > files.Count) throw new InvalidOperationException("GitHub returned an incomplete diff; split the pull request before reviewing.");
                        return JsonSerializer.Serialize(new { pullRequest = pr.RootElement, files });
                    }
                    var marker = "<!-- ai-engineering-platform:" + operationId + " -->";
                    var reviewBody = Get("body") + "\n\n" + marker;
                    var collection = operation == "PublishComment" ? $"/issues/{number}/comments" : $"/pulls/{number}/reviews";
                    using (var previous = await SendAsync(HttpMethod.Get, prefix + collection + "?per_page=100", token, null, ct))
                        foreach (var item in previous.RootElement.EnumerateArray()) if (item.TryGetProperty("body", out var body) && (body.GetString() ?? "").Contains(marker, StringComparison.Ordinal)) return item.GetRawText();
                    if (operation == "PublishComment") result = await SendAsync(HttpMethod.Post, prefix + $"/issues/{number}/comments", token, new { body = reviewBody }, ct);
                    else
                    {
                        var decision = Get("decision");
                        if (decision is not ("APPROVE" or "COMMENT" or "REQUEST_CHANGES")) throw new ArgumentException("Invalid review decision.");
                        result = await SendAsync(HttpMethod.Post, prefix + $"/pulls/{number}/reviews", token, new { body = reviewBody, @event = decision, commit_id = pr.RootElement.GetProperty("head").GetProperty("sha").GetString() }, ct);
                    }
                }
                break;
            default: throw new InvalidOperationException("Source control operation is not permitted.");
        }
        using (result)
        {
            if (operation is "SubmitCodeReview" or "PublishComment" && (!result.RootElement.TryGetProperty("id", out var id) || !id.TryGetInt64(out var confirmedId) || confirmedId <= 0)) throw new InvalidOperationException("GitHub did not return a confirmed publication identifier.");
            return result.RootElement.GetRawText();
        }
    }
}
