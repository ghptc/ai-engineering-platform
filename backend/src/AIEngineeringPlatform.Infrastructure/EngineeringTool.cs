using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using System.Text.Json;
namespace AIEngineeringPlatform.Infrastructure;

public sealed class EngineeringTool(string name, IExecutionSandbox sandbox, ISourceControl source, IPlatformStore store, SandboxExecutionLimits? limits = null) : IAgentTool
{
    public string Name => name;
    public static readonly string[] Names = ["ReadRepository", "ListFiles", "SearchCode", "ReadFile", "ApplyPatch", "WriteFile", "RunCommand", "RunTests", "InspectDiff", "CreateBranch", "CreateCommit", "PushBranch", "CreatePullRequest", "ReadPullRequest", "SubmitCodeReview", "ReadProjectMemory", "SaveArtifact"];
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
        if (Name == "ReadProjectMemory") return new(true, JsonSerializer.Serialize(await store.RecordsAsync("artifact", context.Project.Id, null, null, 0, 0, 100, ct)));
        if (Name == "SaveArtifact")
        {
            if (!args.TryGetProperty("content", out var content)) throw new ArgumentException("content is required.");
            var artifactId = "artifact-" + operationId;
            await store.AppendAsync(new() { Id = artifactId, Kind = "artifact", ProjectId = context.Project.Id, RunId = context.Run.Id, Name = "AgentArtifact", Content = SecretRedactor.Redact(content.GetString() ?? content.GetRawText()) }, ct);
            return new(true, "Artifact persisted.", artifactId);
        }
        var repository = context.Project.RepositoryConnection ?? throw new ConfigurationException("Connect a repository before invoking repository tools.");
        if (Name is "CreateBranch" or "CreatePullRequest" or "ReadPullRequest" or "SubmitCodeReview") return new(true, await source.ExecuteAsync(repository, Name, args, operationId, ct));
        if (Name == "RunTests" && (!args.TryGetProperty("profile", out var profile) || profile.GetString() is not ("backend" or "frontend" or "e2e" or "all"))) throw new ArgumentException("Use a configured backend, frontend, e2e or all test profile.");
        var response = await sandbox.ExecuteAsync(new(operationId, context.Run.Id, repository, Name, args, TimeoutSeconds: limits?.TimeoutSeconds ?? 60, CpuLimit: limits?.CpuLimit ?? 1, MemoryMb: limits?.MemoryMb ?? 512, OutputLimitBytes: limits?.OutputLimitBytes ?? 100000, Fence: context.Run.Version, LeaseOwner: context.Run.LeaseOwner), ct);
        return new(response.Success && (Name != "RunTests" || response.ExitCode == 0), SecretRedactor.Redact(response.Output), response.EvidenceId, response.DiffHash);
    }
}
