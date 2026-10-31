using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.LocalExecutor;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
ExecutorOptions options;
if (builder.Configuration.GetValue("EXECUTOR_MANAGED", false)) options = await ManagedExecutorConfiguration.CreateAsync(builder.Configuration, CancellationToken.None);
else
{
    var configurationPath = builder.Configuration["EXECUTOR_CONFIG"] ?? throw new InvalidOperationException("EXECUTOR_CONFIG must identify an owner-managed executor JSON configuration.");
    builder.Configuration.AddJsonFile(configurationPath, optional: false, reloadOnChange: false);
    options = builder.Configuration.GetSection("Executor").Get<ExecutorOptions>() ?? throw new InvalidOperationException("Executor configuration is required.");
}
options.Validate(builder.Environment.IsEnvironment("Testing"));
Directory.CreateDirectory(options.StateDirectory);
if (new DirectoryInfo(options.StateDirectory).LinkTarget != null) throw new InvalidOperationException("Executor state directory cannot be a symlink.");
File.SetUnixFileMode(options.StateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
Directory.CreateDirectory(Path.Combine(options.StateDirectory, "control-home"));
// One daemon owns the durable operation ledger and Git workspaces.
using var ownership = new FileStream(Path.Combine(options.StateDirectory, "executor.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
var tokenMode = File.GetUnixFileMode(options.TokenFile);
if ((tokenMode & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.GroupWrite)) != 0) throw new InvalidOperationException("Executor token file must not be writable by a group or readable by others.");
var token = (await File.ReadAllTextAsync(options.TokenFile)).Trim();
if (token.Length < 32) throw new InvalidOperationException("Use a random executor token of at least 32 characters.");
var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
var socketDirectory = Path.GetDirectoryName(options.SocketPath)!;
Directory.CreateDirectory(socketDirectory);
if (new DirectoryInfo(socketDirectory).LinkTarget != null) throw new InvalidOperationException("Socket directory cannot be a symlink.");
await UnixSocketFiles.RemoveStaleSocketAsync(options.SocketPath); // Ownership lock acquired; non-socket paths are never deleted.
builder.WebHost.ConfigureKestrel(server => { server.Limits.MaxRequestBodySize = 1_000_000; server.ListenUnixSocket(options.SocketPath); });
builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole();
builder.Services.AddSingleton(options); builder.Services.AddSingleton<ControlledProcess>(); builder.Services.AddSingleton<DockerWorkload>(); builder.Services.AddSingleton<LocalExecutionService>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    var header = context.Request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || header.Length > 1024 || !CryptographicOperations.FixedTimeEquals(tokenHash, SHA256.HashData(Encoding.UTF8.GetBytes(header[7..])))) { context.Response.StatusCode = 401; return; }
    try { await next(); }
    catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException or InvalidOperationException or UnauthorizedAccessException or ConfigurationException or FileNotFoundException or ExecutorFailureException)
    {
        var status = exception is ExecutorFailureException failure ? failure.StatusCode ?? 503 : exception is UnauthorizedAccessException ? 403 : exception is ConfigurationException ? 503 : exception is ArgumentException or System.Text.Json.JsonException or FileNotFoundException ? 400 : 409;
        if (exception is ExecutorFailureException diagnostic)
            app.Logger.LogWarning("Executor operation {OperationId} {Operation} rejected with HTTP {Status} category {Category} reason {Reason} outcomeUnknown {OutcomeUnknown}", diagnostic.OperationId, diagnostic.Operation, status, diagnostic.Category, diagnostic.Reason, diagnostic.OutcomeUnknown);
        await Results.Problem(statusCode: status, title: "Executor rejected operation", detail: "Inspect the operation category and administrator configuration.", extensions: new Dictionary<string, object?> { ["code"] = exception is ExecutorFailureException typed ? typed.Reason switch { ExecutorFailureReason.OperationRetentionLimit => "operation_retention_limit", ExecutorFailureReason.ImageUnavailable => "image_unavailable", ExecutorFailureReason.ProfileTargetMissing => "profile_target_missing", _ => typed.Category switch { ExecutorFailureCategory.ProfileRestriction => "profile_restriction", ExecutorFailureCategory.RepositoryAccess => "repository_access", _ => "executor_rejected" } } : "executor_rejected" }).ExecuteAsync(context);
    }
});
app.MapGet("/health/ready", async (DockerWorkload docker, CancellationToken ct) => { await docker.VerifyHostAsync(ct); return Results.Ok(new { status = "Ready", isolation = "Docker/cgroup-v2/seccomp", network = "none", contractVersion = 2 }); });
app.MapPost("/v1/operations", async (SandboxRequest request, HttpContext context, LocalExecutionService executor, CancellationToken ct) =>
{
    if (context.Request.Headers["Idempotency-Key"] != request.OperationId) return Results.Problem(statusCode: 400, title: "Idempotency key mismatch");
    return Results.Ok(await executor.ExecuteAsync(request, ct));
});
await app.Services.GetRequiredService<DockerWorkload>().RecoverAsync(CancellationToken.None);
var runsDirectory = Path.Combine(options.StateDirectory, "runs");
Directory.CreateDirectory(runsDirectory);
foreach (var directory in Directory.EnumerateDirectories(runsDirectory))
{
    if (new DirectoryInfo(directory).LinkTarget != null) throw new InvalidOperationException("Workspace storage contains a symlink.");
    var stateFile = Path.Combine(directory, "state.json");
    if (File.Exists(stateFile))
    {
        var state = System.Text.Json.JsonSerializer.Deserialize<LocalExecutionService.WorkspaceState>(await File.ReadAllTextAsync(stateFile), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        // Retention never deletes persisted receipts or checkpoints during daemon startup.
        if (state?.LastUsedAt < DateTimeOffset.UtcNow.AddDays(-options.RetentionDays)) app.Logger.LogWarning("Workspace {WorkspaceId} is past retention; preserve it for owner archival", Path.GetFileName(directory));
    }
}
await app.StartAsync();
File.SetUnixFileMode(options.SocketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
await app.WaitForShutdownAsync();
public partial class ExecutorProgram { }
