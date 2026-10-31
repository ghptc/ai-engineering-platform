using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace AIEngineeringPlatform.LocalExecutor;

public sealed class LocalExecutionService(ExecutorOptions options, ControlledProcess process, DockerWorkload docker)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<SandboxResponse> ExecuteAsync(SandboxRequest request, CancellationToken ct)
    {
        ValidateRequest(request);
        var repository = options.Repositories.SingleOrDefault(x => x.Owner == request.Repository.Owner && x.Name == request.Repository.Repository && x.InstallationId == request.Repository.InstallationId && x.DefaultBranch == request.Repository.DefaultBranch) ?? throw new ExecutorFailureException(ExecutorFailureCategory.RepositoryAccess, request.OperationId, request.Operation, 403, false);
        await gate.WaitAsync(ct);
        try
        {
            var runDirectory = Path.Combine(options.StateDirectory, "runs", request.RunId);
            var runsRoot = Path.GetDirectoryName(runDirectory)!;
            Directory.CreateDirectory(runsRoot);
            if (!Directory.Exists(runDirectory) && Directory.EnumerateDirectories(runsRoot).Count() >= 20) throw new InvalidOperationException("Executor workspace limit reached; clean up expired runs before starting another.");
            Directory.CreateDirectory(runDirectory);
            var statePath = Path.Combine(runDirectory, "state.json");
            var state = File.Exists(statePath) ? JsonSerializer.Deserialize<WorkspaceState>(await File.ReadAllTextAsync(statePath, ct), Json)! : new WorkspaceState { RepositoryIdentity = Identity(request.Repository) };
            if (state.RepositoryIdentity != Identity(request.Repository)) throw new UnauthorizedAccessException("An existing workspace cannot change repository identity.");
            if (request.Fence < state.Fence || request.Fence == state.Fence && state.Owner != null && state.Owner != request.LeaseOwner) throw new InvalidOperationException("Stale executor fence or owner.");
            state.Fence = request.Fence; state.Owner = request.LeaseOwner; state.LastUsedAt = DateTimeOffset.UtcNow;
            await SaveAsync(statePath, state, ct);
            var fingerprint = Hash(JsonSerializer.Serialize(new { request.RunId, request.Repository, request.Operation, request.Arguments, request.TimeoutSeconds, request.CpuLimit, request.MemoryMb, request.OutputLimitBytes, request.NetworkEnabled }));
            var resultPath = Path.Combine(runDirectory, request.OperationId + ".json");
            if (File.Exists(resultPath))
            {
                var saved = JsonSerializer.Deserialize<OperationReceipt>(await File.ReadAllTextAsync(resultPath, ct), Json)!;
                if (saved.Fingerprint != fingerprint) throw new InvalidOperationException("An operation id cannot be reused for different arguments.");
                return saved.Response;
            }
            if (Directory.EnumerateFiles(runDirectory, "*.json").Count() > 201) throw new ExecutorFailureException(ExecutorFailureCategory.WorkspaceConflict, request.OperationId, request.Operation, 409, false, ExecutorFailureReason.OperationRetentionLimit);
            var workspace = Path.Combine(runDirectory, "repository");
            if (!Directory.Exists(Path.Combine(workspace, ".git")))
            {
                var environment = await CredentialsAsync(repository, ct);
                var remote = repository.TestRemotePath ?? $"https://github.com/{repository.Owner}/{repository.Name}.git";
                var clone = await GitAsync(null, ["clone", "--depth", "1", "--no-local", "--no-hardlinks", "--single-branch", "--branch", repository.DefaultBranch, "--", remote, workspace], environment, ct, allowTestFile: repository.TestRemotePath != null);
                if (clone.ExitCode != 0) throw new ExecutorFailureException(ExecutorFailureCategory.RepositoryAccess, request.OperationId, request.Operation, 404, false);
                WorkspaceFiles.Enumerate(workspace, options.WorkspaceLimitMb);
                state.BaseCommit = (await GitAsync(workspace, ["rev-parse", "HEAD"], null, ct)).Output.Trim();
                await RequireGitAsync(workspace, ["switch", "-c", "engineering/" + request.RunId], ct);
                await SaveAsync(statePath, state, ct);
            }
            else if (string.IsNullOrEmpty(state.BaseCommit))
            {
                // Interrupted initialization can resume only from the authorized default-branch checkout.
                state.BaseCommit = (await GitAsync(workspace, ["rev-parse", "HEAD"], null, ct)).Output.Trim();
                await RequireGitAsync(workspace, ["switch", "-C", "engineering/" + request.RunId], ct);
                await SaveAsync(statePath, state, ct);
            }
            if (state.BaselineRevision == null)
            {
                var initialStatus = await RequireGitAsync(workspace, ["status", "--porcelain"], ct);
                if (!string.IsNullOrWhiteSpace(initialStatus.Output)) throw new ConfigurationException("Legacy workspace has uncommitted changes; preserve it for human reconciliation before establishing a baseline.");
                state.BaselineRevision = WorkspaceFiles.Revision(workspace, options.WorkspaceLimitMb);
            }
            WorkspaceFiles.Enumerate(workspace, options.WorkspaceLimitMb);
            string Argument(string key) => request.Arguments.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new ArgumentException("Missing string argument: " + key);
            var success = true; int? exitCode = 0; string output;
            OperationExecution? operationExecution = null;
            switch (request.Operation)
            {
                case "ReadRepository":
                    output = JsonSerializer.Serialize(new { repository.Owner, repository.Name, Branch = "engineering/" + request.RunId, state.BaseCommit, HeadCommit = (await RequireGitAsync(workspace, ["rev-parse", "HEAD"], ct)).Output.Trim(), SelectedBranch = state.SelectedBranch, HasSourceChanges = state.BaselineRevision != WorkspaceFiles.Revision(workspace, options.WorkspaceLimitMb), Files = WorkspaceFiles.Enumerate(workspace, options.WorkspaceLimitMb), Capabilities = ProfileSelection.Capabilities(WorkspaceFiles.Enumerate(workspace, options.WorkspaceLimitMb), repository.Profiles), TestProfiles = ProfileSelection.Available(WorkspaceFiles.Enumerate(workspace, options.WorkspaceLimitMb), repository.Profiles) }); break;
                case "SelectBranch":
                    var selected = Argument("branch");
                    var expectedCommit = Argument("commit");
                    if (!Regex.IsMatch(selected, @"\Aengineering/[a-zA-Z0-9-]{1,100}\z") || selected == repository.DefaultBranch || !Regex.IsMatch(expectedCommit, @"\A[a-f0-9]{40}\z")) throw new ArgumentException("Select an existing engineering branch at its exact commit.");
                    var dirty = await RequireGitAsync(workspace, ["status", "--porcelain"], ct);
                    if (!string.IsNullOrWhiteSpace(dirty.Output) || state.Commit != null) throw new InvalidOperationException("Branch reconciliation cannot overwrite workspace changes or a delivery commit.");
                    var fetched = await GitAsync(workspace, ["fetch", "--depth", "1", "origin", "refs/heads/" + selected], await CredentialsAsync(repository, ct), ct, repository.TestRemotePath != null);
                    if (fetched.ExitCode != 0) throw new InvalidOperationException("Cannot fetch selected branch.");
                    var fetchedHead = (await RequireGitAsync(workspace, ["rev-parse", "FETCH_HEAD"], ct)).Output.Trim();
                    if (fetchedHead != expectedCommit) throw new InvalidOperationException("Selected branch changed; inspect repository facts again.");
                    await RequireGitAsync(workspace, ["reset", "--keep", fetchedHead], ct);
                    state.SelectedBranch = selected; state.TestedRevision = null; state.BaselineRevision = WorkspaceFiles.Revision(workspace, options.WorkspaceLimitMb);
                    output = JsonSerializer.Serialize(new { branch = selected, sha = fetchedHead }); break;
                case "CreateBranch":
                    if (Argument("branch") != "engineering/" + request.RunId) throw new ArgumentException("Only the run's dedicated feature branch is permitted.");
                    output = "engineering/" + request.RunId; break;
                case "ListFiles": output = string.Join('\n', WorkspaceFiles.Enumerate(workspace, options.WorkspaceLimitMb)); break;
                case "ReadFile":
                    var target = WorkspaceFiles.Resolve(workspace, Argument("path"));
                    if (new FileInfo(target).Length > request.OutputLimitBytes) throw new ArgumentException("ReadFile exceeds output bounds; use a smaller file.");
                    output = await File.ReadAllTextAsync(target, ct); break;
                case "SearchCode":
                    var query = Argument("query");
                    if (query.Length is < 1 or > 200) throw new ArgumentException("Search query must contain 1–200 characters.");
                    var matches = new StringBuilder();
                    foreach (var file in WorkspaceFiles.Enumerate(workspace, options.WorkspaceLimitMb))
                    {
                        var line = 0;
                        foreach (var text in File.ReadLines(WorkspaceFiles.Resolve(workspace, file)))
                        {
                            line++; if (text.Contains(query, StringComparison.Ordinal)) matches.AppendLine($"{file}:{line}:{text}");
                            if (Encoding.UTF8.GetByteCount(matches.ToString()) > request.OutputLimitBytes) throw new InvalidOperationException("Search output exceeds bounds; narrow the query.");
                        }
                    }
                    output = matches.ToString(); break;
                case "PrepareFrontendLock":
                    if (request.Arguments.EnumerateObject().Any()) throw new ArgumentException("PrepareFrontendLock accepts no paths, commands or package arguments.");
                    output = ApprovedFrontendBootstrap.Prepare(workspace); break;
                case "WriteFile": WorkspaceFiles.Write(workspace, Argument("path"), Argument("content")); output = "Written " + Argument("path"); break;
                case "ApplyPatch":
                    // patch is an encoded object to retain the original common tool envelope.
                    using (var patch = JsonDocument.Parse(Argument("patch")))
                        WorkspaceFiles.Patch(workspace, patch.RootElement.GetProperty("path").GetString()!, patch.RootElement.GetProperty("search").GetString()!, patch.RootElement.GetProperty("replacement").GetString()!);
                    output = "Patch applied"; break;
                case "InspectDiff":
                    var files = WorkspaceFiles.Enumerate(workspace, options.WorkspaceLimitMb);
                    if (files.Length > 0) await RequireGitAsync(workspace, ["add", "--intent-to-add", "--force", "--", .. files], ct);
                    output = (await RequireGitAsync(workspace, ["diff", "--no-ext-diff", "--no-textconv", state.BaseCommit!, "--", .. await SourcePathsAsync(workspace, ct)], ct)).Output; break;
                case "RunCommand":
                case "RunTests":
                    var name = Argument("profile");
                    if (request.Operation == "RunTests" && name is not ("all" or "backend" or "frontend" or "e2e" or "node") || request.Operation == "RunCommand" && name is not ("build" or "lint" or "format" or "typecheck")) throw new ArgumentException("Unregistered execution profile.");
                    if (request.Arguments.EnumerateObject().Any(x => x.Name != "profile")) throw new ArgumentException("Only a profile identifier may be supplied; shell text is forbidden.");
                    var sourceFilesForProfile = WorkspaceFiles.Enumerate(workspace, options.WorkspaceLimitMb);
                    if (name == "all")
                    {
                        var available = ProfileSelection.Available(sourceFilesForProfile, repository.Profiles);
                        name = available.Contains("all") ? "all" : available.Length == 1 ? available[0] : throw new ExecutorFailureException(ExecutorFailureCategory.ProfileRestriction, request.OperationId, request.Operation, 422, false);
                    }
                    if (!repository.Profiles.TryGetValue(name, out var profile)) throw new ExecutorFailureException(ExecutorFailureCategory.ProfileRestriction, request.OperationId, request.Operation, 422, false);
                    if (name == "node" && !profile.Command.SequenceEqual(new[] { "node", "--test" })) throw new ExecutorFailureException(ExecutorFailureCategory.ProfileRestriction, request.OperationId, request.Operation, 422, false);
                    if (!(request.Operation == "RunTests" ? ProfileSelection.Applicable(sourceFilesForProfile, profile) : ProfileSelection.ApplicableCommand(sourceFilesForProfile, profile))) throw new ExecutorFailureException(ExecutorFailureCategory.ProfileRestriction, request.OperationId, request.Operation, 422, false, ExecutorFailureReason.ProfileTargetMissing);
                    ProcessResult execution;
                    try { execution = await docker.ExecuteAsync(workspace, profile, request, ct); }
                    catch (InvalidOperationException) { throw new ExecutorFailureException(ExecutorFailureCategory.Infrastructure, request.OperationId, request.Operation, 503, true); }
                    operationExecution = new(name, profile.Command, "/workspace", SecretRedactor.Redact(execution.Stdout), SecretRedactor.Redact(execution.Stderr), profile.Image);
                    output = execution.Output; exitCode = execution.ExitCode; success = exitCode == 0 && !execution.TimedOut && !execution.OutputExceeded;
                    if (request.Operation == "RunTests")
                    {
                        var managed = profile.Command.Length == 3 && profile.Command[0] == "python3" && profile.Command[1] == "/opt/aep/profiles.py";
                        var count = managed ? DockerWorkload.ManagedTestCount(output, exitCode.Value, name) : DockerWorkload.TestCount(output);
                        success &= count > 0;
                        output += $"\nExecutor evidence: tests={count}; exit={exitCode}; timeout={execution.TimedOut}; outputLimit={execution.OutputExceeded}";
                        if (success) state.TestedRevision = WorkspaceFiles.Revision(workspace, options.WorkspaceLimitMb);
                    }
                    output += "\nWorkload outputs are temporary; persist source changes with WriteFile or ApplyPatch.";
                    break;
                case "CreateCommit":
                    var revision = WorkspaceFiles.Revision(workspace, options.WorkspaceLimitMb);
                    if (Argument("branch") != "engineering/" + request.RunId || Argument("expectedRevision") != revision || state.TestedRevision != revision) throw new InvalidOperationException("Commit requires the tested source revision and dedicated feature branch.");
                    var commitMessage = Argument("message");
                    if (commitMessage.Length is < 1 or > 500) throw new ArgumentException("Commit message is outside bounds.");
                    var marker = "AEP-Operation: " + request.OperationId;
                    var previousMessage = (await RequireGitAsync(workspace, ["log", "-1", "--format=%B"], ct)).Output;
                    if (!previousMessage.Contains(marker, StringComparison.Ordinal))
                    {
                        var sourceFiles = await SourcePathsAsync(workspace, ct);
                        await RequireGitAsync(workspace, ["add", "--all", "--force", "--", .. sourceFiles], ct);
                        await RequireGitAsync(workspace, ["-c", "user.name=AI Engineering Platform", "-c", "user.email=engineering-platform@localhost", "commit", "--no-gpg-sign", "-m", commitMessage + "\n\n" + marker], ct);
                    }
                    state.Commit = (await RequireGitAsync(workspace, ["rev-parse", "HEAD"], ct)).Output.Trim();
                    state.CommittedRevision = revision;
                    state.BaselineRevision = revision;
                    output = JsonSerializer.Serialize(new { sha = state.Commit, branch = "engineering/" + request.RunId }); break;
                case "PushBranch":
                    if (Argument("branch") != "engineering/" + request.RunId || state.Commit == null || state.CommittedRevision != WorkspaceFiles.Revision(workspace, options.WorkspaceLimitMb) || state.CommittedRevision != state.TestedRevision) throw new InvalidOperationException("Push requires the committed and tested feature-branch revision.");
                    var push = await GitAsync(workspace, ["push", "origin", "HEAD:refs/heads/engineering/" + request.RunId], await CredentialsAsync(repository, ct), ct, repository.TestRemotePath != null);
                    if (push.ExitCode != 0) throw new InvalidOperationException("Feature branch push rejected: " + SecretRedactor.Redact(push.Output));
                    output = JsonSerializer.Serialize(new { sha = state.Commit, branch = "engineering/" + request.RunId, pushed = true }); break;
                default: throw new UnauthorizedAccessException("Executor operation is not registered.");
            }
            WorkspaceFiles.Enumerate(workspace, options.WorkspaceLimitMb);
            output = SecretRedactor.Redact(output);
            if (Encoding.UTF8.GetByteCount(output) > request.OutputLimitBytes) { output = "Operation output exceeded its configured bound."; success = false; exitCode = 125; }
            var response = new SandboxResponse(success, output, "local-" + request.OperationId, exitCode, request.RunId, WorkspaceFiles.Revision(workspace, options.WorkspaceLimitMb), operationExecution);
            await SaveAsync(statePath, state, ct);
            await SaveAsync(resultPath, new OperationReceipt(fingerprint, response), ct);
            return response;
        }
        finally { gate.Release(); }
    }
    public static void ValidateRequest(SandboxRequest request)
    {
        if (request.Repository is null || request.Operation is not ("ReadRepository" or "SelectBranch" or "CreateBranch" or "ListFiles" or "ReadFile" or "SearchCode" or "WriteFile" or "PrepareFrontendLock" or "ApplyPatch" or "InspectDiff" or "RunTests" or "RunCommand" or "CreateCommit" or "PushBranch") || !Regex.IsMatch(request.RunId, "\\A[a-zA-Z0-9-]{1,64}\\z") || !Regex.IsMatch(request.OperationId, "\\A[a-zA-Z0-9-]{1,128}\\z") || request.Arguments.ValueKind != JsonValueKind.Object || request.Arguments.GetRawText().Length > 100000 || request.NetworkEnabled || request.Fence < 1 || string.IsNullOrWhiteSpace(request.LeaseOwner) || request.LeaseOwner.Length > 128 || request.TimeoutSeconds is < 1 or > 120 || request.MemoryMb is < 128 or > 4096 || request.CpuLimit is < 1 or > 4 || request.OutputLimitBytes is < 1024 or > 100000) throw new ArgumentException("Invalid bounded executor request; networking is disabled.");
    }
    private static string Identity(RepositoryConnection repository) => $"{repository.Owner}/{repository.Repository}@{repository.InstallationId}:{repository.DefaultBranch}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static async Task SaveAsync<T>(string path, T data, CancellationToken ct)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(data, Json), ct);
        File.Move(temporary, path, overwrite: true);
    }
    private async Task<Dictionary<string, string>?> CredentialsAsync(AuthorizedRepository repository, CancellationToken ct)
    {
        if (repository.TestRemotePath != null) return null;
        if (string.IsNullOrWhiteSpace(options.GitHubAppId) || string.IsNullOrWhiteSpace(options.GitHubPrivateKeyPath)) throw new ConfigurationException("GitHub App credentials are required for authorized Git transfers.");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        var github = new GitHubSourceControl(http, options.GitHubAppId, options.GitHubPrivateKeyPath, null);
        var token = await github.CreateInstallationTokenAsync(new(repository.Owner, repository.Name, repository.InstallationId, repository.DefaultBranch), ct, contentsOnly: true);
        return new() { ["GIT_CONFIG_COUNT"] = "1", ["GIT_CONFIG_KEY_0"] = "http.https://github.com/.extraheader", ["GIT_CONFIG_VALUE_0"] = "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + token)) };
    }
    private async Task<string[]> SourcePathsAsync(string workspace, CancellationToken ct)
    {
        var tracked = await RequireGitAsync(workspace, ["ls-files", "-z"], ct);
        var paths = tracked.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(WorkspaceFiles.IsSource).Union(WorkspaceFiles.Enumerate(workspace, options.WorkspaceLimitMb)).ToArray();
        foreach (var path in paths) WorkspaceFiles.Resolve(workspace, path);
        return paths;
    }
    private Task<ProcessResult> GitAsync(string? workspace, string[] arguments, IReadOnlyDictionary<string, string>? environment, CancellationToken ct, bool allowTestFile = false) => process.RunAsync("/usr/bin/git", ["-c", "core.hooksPath=/dev/null", "-c", "core.fsmonitor=false", "-c", "credential.helper=", "-c", "protocol.ext.allow=never", "-c", "protocol.file.allow=" + (allowTestFile ? "always" : "never"), .. arguments], workspace, 60, 100000, ct, environment: environment);
    private async Task<ProcessResult> RequireGitAsync(string workspace, string[] arguments, CancellationToken ct)
    {
        var result = await GitAsync(workspace, arguments, null, ct);
        if (result.ExitCode != 0) throw new InvalidOperationException("Controlled Git operation failed: " + SecretRedactor.Redact(result.Output));
        return result;
    }
    public sealed class WorkspaceState
    {
        public string RepositoryIdentity { get; set; } = "";
        public long Fence { get; set; }
        public string? Owner { get; set; }
        public string? BaseCommit { get; set; }
        public string? SelectedBranch { get; set; }
        public string? BaselineRevision { get; set; }
        public string? TestedRevision { get; set; }
        public string? CommittedRevision { get; set; }
        public string? Commit { get; set; }
        public DateTimeOffset LastUsedAt { get; set; }
    }
    public sealed record OperationReceipt(string Fingerprint, SandboxResponse Response);
}
