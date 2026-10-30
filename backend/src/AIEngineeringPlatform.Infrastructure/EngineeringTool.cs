using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using System.Text.Json;
namespace AIEngineeringPlatform.Infrastructure;

public sealed class EngineeringTool(string name, IExecutionSandbox sandbox, ISourceControl source, IPlatformStore store, SandboxExecutionLimits? limits = null) : IAgentTool
{
    public string Name => name;
    public static readonly string[] Names = ["ReadRepository", "ReconcileRepository", "SelectBranch", "ListFiles", "SearchCode", "ReadFile", "ApplyPatch", "WriteFile", "RunCommand", "RunTests", "InspectDiff", "CreateBranch", "CreateCommit", "PushBranch", "CreatePullRequest", "ReadPullRequest", "SubmitCodeReview", "ReadProjectMemory", "SaveArtifact"];
    public async Task<ToolResult> ExecuteAsync(AgentContext context, JsonElement args, string operationId, CancellationToken ct)
    {
        if (args.ValueKind != JsonValueKind.Object || args.GetRawText().Length > 100000) throw new ArgumentException("Tool arguments must be a bounded JSON object.");
        if (!context.Definition.AllowedTools.Contains(Name)) throw new UnauthorizedAccessException("Tool is not authorized for this role.");
        if (args.TryGetProperty("path", out var path))
        {
            var value = path.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains('\\') || value.Split('/').Any(x => x is ".." or ".git" || x.StartsWith(".env", StringComparison.OrdinalIgnoreCase)) || new[] { ".pem", ".key", ".pfx", ".p12" }.Contains(Path.GetExtension(value), StringComparer.OrdinalIgnoreCase) || value.Contains('\0')) throw new ArgumentException("Unsafe repository path.");
        }
        if (Name is "ReadFile" or "WriteFile" && !args.TryGetProperty("path", out _)) throw new ArgumentException("path is required.");
        if (Name == "WriteFile" && !args.TryGetProperty("content", out _)) throw new ArgumentException("content is required.");
        if (Name == "ApplyPatch" && !args.TryGetProperty("patch", out _)) throw new ArgumentException("patch is required.");
        if (Name == "RunCommand" && (!args.TryGetProperty("profile", out var commandProfile) || commandProfile.GetString() is not ("build" or "lint" or "format" or "typecheck") || args.EnumerateObject().Any(x => x.Name != "profile"))) throw new UnauthorizedAccessException("RunCommand accepts only administrator-defined build, lint, format or typecheck profiles; shell text is not allowed.");
        if (Name == "ReadProjectMemory")
        {
            var kind = args.TryGetProperty("kind", out var k) ? k.GetString()! : "artifact";
            if (kind is not ("artifact" or "tool" or "task" or "message" or "requirement" or "handoff" or "project")) throw new ArgumentException("Unsupported memory kind.");
            var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
            var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 2000;
            if (offset < 0 || limit is < 1 or > 4000) throw new ArgumentException("Use nonnegative offset and 1–4000 characters per page.");
            if (kind == "project")
            {
                if (!args.TryGetProperty("id", out var projectId) || projectId.GetString() != context.Project.Id) throw new KeyNotFoundException("Project knowledge not found.");
                var original = context.Project.OriginalContext;
                return new(true, JsonSerializer.Serialize(new { Id = context.Project.Id, Kind = "project", offset, TotalCharacters = original.Length, Content = original.Substring(Math.Min(offset, original.Length), Math.Min(limit, Math.Max(0, original.Length - offset))) }));
            }
            if (args.TryGetProperty("id", out var id))
            {
                var record = await store.GetRecordAsync(kind, id.GetString()!, ct);
                if (record == null || record.ProjectId != context.Project.Id) throw new KeyNotFoundException("Memory not found in this project.");
                return new(true, JsonSerializer.Serialize(new { Reference = ContextBudget.Reference(record), offset, TotalCharacters = record.Content.Length, Content = record.Content.Substring(Math.Min(offset, record.Content.Length), Math.Min(limit, Math.Max(0, record.Content.Length - offset))) }));
            }
            var records = await store.RecordsAsync(kind, context.Project.Id, args.TryGetProperty("runId", out var runId) ? runId.GetString() : context.Run.CurrentTask == null ? null : context.Run.Id, null, 0, offset, 20, ct);
            return new(true, JsonSerializer.Serialize(records.Select(ContextBudget.Reference)));
        }
        if (Name == "SaveArtifact")
        {
            if (!args.TryGetProperty("content", out var content)) throw new ArgumentException("content is required.");
            var artifactId = "artifact-" + operationId;
            await store.AppendAsync(new() { Id = artifactId, Kind = "artifact", ProjectId = context.Project.Id, RunId = context.Run.Id, Name = "AgentArtifact", Content = SecretRedactor.Redact(content.GetString() ?? content.GetRawText()) }, ct);
            return new(true, "Artifact persisted.", artifactId);
        }
        var repository = context.Project.RepositoryConnection ?? throw new ConfigurationException("Connect a repository before invoking repository tools.");
        if (Name == "SelectBranch")
        {
            if (!args.TryGetProperty("branch", out var selected) || !args.TryGetProperty("commit", out var expected)) throw new ArgumentException("Exact branch and commit are required.");
            using var inventory = JsonDocument.Parse(await source.ExecuteAsync(repository, "ReconcileRepository", JsonSerializer.SerializeToElement(new { }), operationId, ct));
            if (!(inventory.RootElement.TryGetProperty("branches", out var branches) && branches.EnumerateArray().Any(x => x.GetProperty("branch").GetString() == selected.GetString() && x.GetProperty("commit").GetString() == expected.GetString())) && !inventory.RootElement.GetProperty("pullRequests").EnumerateArray().Any(x => x.GetProperty("branch").GetString() == selected.GetString() && x.GetProperty("commit").GetString() == expected.GetString() && x.GetProperty("state").GetString() == "open")) throw new ArgumentException("Only an exact current same-repository feature branch or open PR head may be selected.");
        }
        if (Name is "ReconcileRepository" or "CreateBranch" or "CreatePullRequest" or "ReadPullRequest" or "SubmitCodeReview") return new(true, await source.ExecuteAsync(repository, Name, args, operationId, ct));
        if (Name == "RunTests" && (!args.TryGetProperty("profile", out var profile) || profile.GetString() is not ("backend" or "frontend" or "e2e" or "all" or "node"))) throw new ArgumentException("Use a configured node, backend, frontend, e2e or all test profile.");
        var response = await sandbox.ExecuteAsync(new(operationId, context.Run.Id, repository, Name, args, TimeoutSeconds: limits?.TimeoutSeconds ?? 60, CpuLimit: limits?.CpuLimit ?? 1, MemoryMb: limits?.MemoryMb ?? 512, OutputLimitBytes: limits?.OutputLimitBytes ?? 100000, Fence: context.Run.Version, LeaseOwner: context.Run.LeaseOwner, MaxTransientRetries: Math.Clamp(context.Run.Policy.MaxRetries, 0, 2)), ct);
        return new(response.Success && (Name != "RunTests" || response.ExitCode == 0), SecretRedactor.Redact(response.Output), response.EvidenceId, response.DiffHash, response.ExitCode, response.Execution == null ? null : response.Execution with { Command = response.Execution.Command.Select(SecretRedactor.Redact).ToArray(), Stdout = SecretRedactor.Redact(response.Execution.Stdout), Stderr = SecretRedactor.Redact(response.Execution.Stderr) });
    }
}
