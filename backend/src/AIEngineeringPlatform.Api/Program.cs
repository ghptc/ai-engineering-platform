using AIEngineeringPlatform.Api;
using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using AIEngineeringPlatform.Agents;
using AIEngineeringPlatform.Orchestration;
using AIEngineeringPlatform.Worker;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole();
builder.Services.AddOptions<OwnerOptions>().Configure(o => { o.Mode = builder.Configuration["APPLICATION_AUTH_MODE"] ?? "Owner"; o.CredentialHash = builder.Configuration["APPLICATION_OWNER_CREDENTIAL_SECRET"] ?? ""; })
    .Validate(o => o.Mode == "Owner" && OwnerCredentials.ValidFormat(o.CredentialHash), "Owner authentication requires a PBKDF2 credential hash; see README.").ValidateOnStart();
builder.Services.AddOptions<ExecutionDefaults>().Configure(o =>
{
    o.MaxIterations = builder.Configuration.GetValue("EXECUTION_MAX_ITERATIONS", 5);
    o.MaxToolCalls = builder.Configuration.GetValue("EXECUTION_MAX_TOOL_CALLS", 30);
    o.MaxDurationMinutes = builder.Configuration.GetValue("EXECUTION_MAX_DURATION_MINUTES", 60);
    o.MaxBudgetUsd = builder.Configuration.GetValue("EXECUTION_MAX_BUDGET_USD", 5m);
}).Validate(o => o.IsValid(), "Invalid default execution limits.").ValidateOnStart();
var mongo = builder.Configuration["MONGODB_CONNECTION_STRING"];
if (string.IsNullOrWhiteSpace(mongo)) throw new InvalidOperationException("MONGODB_CONNECTION_STRING is required.");
builder.Services.AddSingleton<IPlatformStore>(new MongoPlatformStore(mongo, builder.Configuration["MONGODB_DATABASE"] ?? "ai_engineering_platform"));
builder.Services.AddSingleton<IWorkspaceQueries>(sp => (MongoPlatformStore)sp.GetRequiredService<IPlatformStore>());
builder.Services.Configure<LanguageModelOptions>(builder.Configuration.GetSection("LanguageModel"));
builder.Services.AddSingleton<ILanguageModelProvider>(sp => new OpenAIProvider(builder.Configuration["OPENAI_API_KEY"], sp.GetRequiredService<IOptions<LanguageModelOptions>>().Value));
builder.Services.AddHttpClient("sandbox", h => h.Timeout = TimeSpan.FromSeconds(140)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient("github", h => h.Timeout = TimeSpan.FromSeconds(60)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddOptions<RemoteSandboxOptions>().Configure(o => { o.Provider = builder.Configuration["EXECUTION_SANDBOX_PROVIDER"] ?? "Remote"; o.Endpoint = builder.Configuration["EXECUTION_SANDBOX_ENDPOINT"]; o.Token = builder.Configuration["EXECUTION_SANDBOX_TOKEN"]; o.SocketPath = builder.Configuration["EXECUTION_SANDBOX_SOCKET_PATH"]; }).Validate(o => o.Provider is "Remote" or "LocalUnix", "Use Remote or LocalUnix sandbox provider.").ValidateOnStart();
builder.Services.AddSingleton<IExecutionSandbox>(sp => { var options = sp.GetRequiredService<IOptions<RemoteSandboxOptions>>().Value; return options.Provider == "LocalUnix" ? new UnixExecutionSandbox(options.SocketPath ?? "", options.Token ?? "") : new RemoteExecutionSandbox(sp.GetRequiredService<IHttpClientFactory>().CreateClient("sandbox"), options.Endpoint, options.Token); });
builder.Services.AddSingleton<ISourceControl>(sp => new GitHubSourceControl(sp.GetRequiredService<IHttpClientFactory>().CreateClient("github"), builder.Configuration["GITHUB_APP_ID"], builder.Configuration["GITHUB_APP_PRIVATE_KEY_PATH"], builder.Configuration["GITHUB_WEBHOOK_SECRET"]));
foreach (var role in Enum.GetValues<AgentRole>()) builder.Services.AddSingleton<IEngineeringAgent>(sp => new EngineeringAgent(role, sp.GetRequiredService<ILanguageModelProvider>()));
builder.Services.AddOptions<SandboxExecutionLimits>().Bind(builder.Configuration.GetSection("SandboxExecution")).Validate(o => o.TimeoutSeconds is >= 1 and <= 120 && o.MemoryMb is >= 128 and <= 4096 && o.CpuLimit is >= 1 and <= 4 && o.OutputLimitBytes is >= 1024 and <= 100000, "Invalid sandbox execution limits.").ValidateOnStart();
foreach (var name in EngineeringTool.Names) builder.Services.AddSingleton<IAgentTool>(sp => new EngineeringTool(name, sp.GetRequiredService<IExecutionSandbox>(), sp.GetRequiredService<ISourceControl>(), sp.GetRequiredService<IPlatformStore>(), sp.GetRequiredService<IOptions<SandboxExecutionLimits>>().Value));
builder.Services.AddSingleton<GitHubWebhookProcessor>(); builder.Services.AddSingleton<ConversationResponder>(); builder.Services.AddSingleton<PlatformService>(); builder.Services.AddSingleton<ContextBuilder>(); builder.Services.AddSingleton<EngineeringWorkflow>(); builder.Services.AddSingleton<IExecutionPublisher, SignalRPublisher>();
builder.Services.AddHostedService<EngineeringWorker>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "aep.owner"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(o => o.AddPolicy("Owner", p => p.RequireAuthenticatedUser().RequireClaim("role", "owner")));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails(); builder.Services.AddOpenApi(); builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 32768);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter((context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown") + (HttpMethods.IsGet(context.Request.Method) ? ":read" : ":mutate"), _ => new() { PermitLimit = HttpMethods.IsGet(context.Request.Method) ? 600 : 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddFixedWindowLimiter("login", x => { x.PermitLimit = 5; x.Window = TimeSpan.FromMinutes(1); x.QueueLimit = 0; });
});
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1_000_000);
var app = builder.Build();
app.UseExceptionHandler(error => error.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = exception switch
    {
        KeyNotFoundException => (404, "Resource not found"),
        ArgumentException or JsonException or BadHttpRequestException => (400, "Validation failed"),
        ConcurrencyException => (409, "Concurrency conflict"),
        UnauthorizedAccessException => (403, "Forbidden"),
        InvalidOperationException => (409, "Operation cannot be performed"),
        ConfigurationException => (503, "Integration unavailable"),
        _ => (500, "Unexpected server error")
    };
    await Results.Problem(statusCode: status, title: title, detail: status == 500 ? "Use the correlation identifier to locate operational diagnostics." : exception?.Message, extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
}));
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    if (context.HttpContext.Request.Path.StartsWithSegments("/api") || context.HttpContext.Request.Path.StartsWithSegments("/hubs")) await Results.Problem(statusCode: response.StatusCode, title: response.StatusCode switch { 401 => "Authentication required", 403 => "Forbidden", 429 => "Rate limit exceeded", _ => "Request failed" }).ExecuteAsync(context.HttpContext);
});
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff"; ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' ws: wss:; worker-src 'self' blob:; frame-ancestors 'none'";
    ctx.Response.Headers["X-Correlation-Id"] = ctx.TraceIdentifier;
    if (!app.Environment.IsDevelopment()) ctx.Response.Headers["Strict-Transport-Security"] = "max-age=31536000";
    // Same-origin custom headers plus SameSite cookies protect all REST mutations, including login.
    if (ctx.Request.Path.StartsWithSegments("/api") && ctx.Request.Path != "/api/v1/webhooks/github" && (HttpMethods.IsPost(ctx.Request.Method) || HttpMethods.IsPut(ctx.Request.Method) || HttpMethods.IsPatch(ctx.Request.Method) || HttpMethods.IsDelete(ctx.Request.Method)))
    {
        if (ctx.Request.Headers["X-Requested-With"] != "AIEngineeringPlatform") { await Results.Problem(statusCode: 403, title: "Missing same-origin request header").ExecuteAsync(ctx); return; }
    }
    if (ctx.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Headers.TryGetValue("Origin", out var origin) && (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var originUri) || !string.Equals(originUri.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase))) { await Results.Problem(statusCode: 403, title: "Cross-origin hub connections are not allowed").ExecuteAsync(ctx); return; }
    await next();
});
app.UseDefaultFiles(); app.UseStaticFiles(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
app.MapPost("/api/v1/auth/login", async (LoginRequest request, HttpContext context, IOptions<OwnerOptions> options) =>
{
    if (!OwnerCredentials.Verify(request.Password, options.Value.CredentialHash)) return Results.Problem(statusCode: 401, title: "Invalid credentials");
    await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "owner"), new Claim("role", "owner")], CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(new { name = "owner" });
}).RequireRateLimiting("login");
app.MapGet("/api/v1/auth/me", () => new { name = "owner" }).RequireAuthorization("Owner");
app.MapPost("/api/v1/auth/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization("Owner");
var api = app.MapGroup("/api/v1").RequireAuthorization("Owner").RequireRateLimiting("api");
api.MapGet("/projects", async (IPlatformStore s, int? offset, int? limit, CancellationToken ct) => new Page<Project>(await s.ProjectsAsync(Offset(offset), Limit(limit), ct), Offset(offset), Limit(limit)));
api.MapPost("/projects", async (ProjectInput input, PlatformService s, IOptions<ExecutionDefaults> defaults, CancellationToken ct) => { var p = await s.SaveProjectAsync(input.ToProject() with { ExecutionPolicy = input.ExecutionPolicy ?? defaults.Value.Policy() }, null, ct); return Results.Created($"/api/v1/projects/{p.Id}", p); });
api.MapGet("/projects/{id}", (string id, PlatformService s, CancellationToken ct) => s.ProjectAsync(id, ct));
api.MapPut("/projects/{id}", async (string id, ProjectInput input, PlatformService s, CancellationToken ct) => { var existing = await s.ProjectAsync(id, ct); return await s.SaveProjectAsync(input.ToProject() with { Id = id, CreatedAt = existing.CreatedAt, RepositoryConnection = existing.RepositoryConnection, StandingInstructions = existing.StandingInstructions }, input.Version, ct); });
api.MapDelete("/projects/{id}", async (string id, PlatformService s, IPlatformStore store, CancellationToken ct) =>
{
    if ((await store.RunsAsync(id, 0, 1000, ct)).Any(x => x.CurrentStage is not (RunStage.Completed or RunStage.Cancelled))) throw new InvalidOperationException("Cancel active executions before archiving a project.");
    var p = await s.ProjectAsync(id, ct); await s.SaveProjectAsync(p with { Status = "Archived" }, p.Version, ct); return Results.NoContent();
});
api.MapPost("/projects/{id}/contexts", async (string id, ContentInput input, PlatformService service, IPlatformStore store, CancellationToken ct) =>
{
    var p = await service.ProjectAsync(id, ct); ValidateContent(input.Content);
    await service.SaveProjectAsync(p with { OriginalContext = p.OriginalContext + "\n\n" + input.Content }, p.Version, ct);
    return Results.Accepted($"/api/v1/projects/{id}");
});
api.MapGet("/projects/{id}/memory", async (string id, PlatformService service, IPlatformStore store, CancellationToken ct) => { await service.ProjectAsync(id, ct); return await store.RecordsAsync("artifact", id, null, null, 0, 0, 100, ct); });
api.MapGet("/projects/{id}/requirements", (string id, IPlatformStore store, int? offset, int? limit, CancellationToken ct) => Records("requirement", id, null, null, 0, offset, limit, store, ct));
api.MapPost("/projects/{id}/requirements", async (string id, RequirementInput input, PlatformService service, IPlatformStore store, CancellationToken ct) =>
{
    await service.ProjectAsync(id, ct); ValidateContent(input.Title); if (input.AcceptanceCriteria.Length == 0) throw new ArgumentException("Acceptance criteria are required.");
    var entry = new RecordEntry { Kind = "requirement", ProjectId = id, Name = input.Title, Content = JsonSerializer.Serialize(input), Status = "Pending" }; await store.AppendAsync(entry, ct); return Results.Created($"/api/v1/requirements/{entry.Id}", entry);
});
api.MapPatch("/requirements/{id}", async (string id, RequirementInput input, IPlatformStore store, CancellationToken ct) =>
{
    var previous = await store.GetRecordAsync("requirement", id, ct) ?? throw new KeyNotFoundException(); ValidateContent(input.Title);
    var revision = previous with { Id = Guid.NewGuid().ToString("N"), Name = input.Title, Content = JsonSerializer.Serialize(input), CreatedAt = DateTimeOffset.UtcNow, Metadata = new() { ["supersedes"] = id } };
    await store.AppendAsync(revision, ct); return revision;
});
api.MapPost("/projects/{id}/tasks", async (string id, TaskQueueInput input, HttpContext ctx, PlatformService service, CancellationToken ct) =>
{
    var run = await service.StartTasksAsync(id, ctx.Request.Headers["Idempotency-Key"].ToString(), input.Tasks, ct);
    return Results.Accepted($"/api/v1/runs/{run.Id}", run);
});
api.MapPatch("/projects/{id}/instructions", async (string id, ContentInput input, PlatformService service, IPlatformStore store, CancellationToken ct) =>
{
    var project = await service.ProjectAsync(id, ct);
    await store.AppendAsync(new() { Kind = "instructionVersion", ProjectId = id, Name = "ProjectInstructions", Content = project.StandingInstructions }, ct);
    return await service.SaveProjectAsync(project with { StandingInstructions = input.Content }, project.Version, ct);
});
api.MapPost("/projects/{id}/runs", async (string id, HttpContext ctx, PlatformService service, CancellationToken ct) =>
{
    var run = await service.StartAsync(id, ctx.Request.Headers["Idempotency-Key"].ToString(), ct); return Results.Accepted($"/api/v1/runs/{run.Id}", run);
});
api.MapGet("/projects/{id}/runs", async (string id, IPlatformStore store, int? offset, int? limit, string? status, CancellationToken ct) => new Page<ExecutionRun>(await store.RunsAsync(id == "all" ? null : id, Offset(offset), Limit(limit), ct, status), Offset(offset), Limit(limit)));
api.MapGet("/runs/{id}", (string id, PlatformService s, CancellationToken ct) => s.RunAsync(id, ct));
api.MapGet("/runs/{id}/events", (string id, long? after, int? offset, int? limit, IPlatformStore store, CancellationToken ct) => Records("event", null, id, null, after ?? 0, offset, limit, store, ct));
api.MapGet("/runs/{id}/handoffs", (string id, int? offset, int? limit, IPlatformStore store, CancellationToken ct) => Records("handoff", null, id, null, 0, offset, limit, store, ct));
api.MapGet("/runs/{id}/artifacts", (string id, int? offset, int? limit, IPlatformStore store, CancellationToken ct) => Records("artifact", null, id, null, 0, offset, limit, store, ct));
foreach (var action in new[] { "pause", "resume", "cancel", "retry" }) { var captured = action; api.MapPost("/runs/{id}/" + action, (string id, PlatformService service, CancellationToken ct) => service.ControlAsync(id, captured, ct)); }
api.MapGet("/projects/{id}/conversations", (string id, IPlatformStore store, int? offset, int? limit, CancellationToken ct) => Records("conversation", id, null, null, 0, offset, limit, store, ct));
api.MapPost("/projects/{id}/conversations", async (string id, PlatformService service, IPlatformStore store, CancellationToken ct) => { await service.ProjectAsync(id, ct); var entry = new RecordEntry { Kind = "conversation", ProjectId = id, Name = "ClientConversation" }; await store.AppendAsync(entry, ct); return Results.Created($"/api/v1/conversations/{entry.Id}/messages", entry); });
api.MapGet("/conversations/{id}/messages", (string id, IPlatformStore store, int? offset, int? limit, CancellationToken ct) => Records("message", null, null, id, 0, offset, limit, store, ct));
api.MapPost("/conversations/{id}/messages", (string id, ContentInput input, PlatformService service, CancellationToken ct) => service.MessageAsync(id, "Client", input.Content, ct));
api.MapPost("/conversations/{id}/reply", (string id, ConversationResponder responder, CancellationToken ct) => responder.ReplyAsync(id, ct));
api.MapGet("/agents", (IPlatformStore store, CancellationToken ct) => store.AgentsAsync(ct));
api.MapGet("/agents/{id}", async (string id, IPlatformStore store, CancellationToken ct) => (await store.AgentsAsync(ct)).SingleOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException());
api.MapPatch("/agents/{id}/configuration", async (string id, AgentInput input, IPlatformStore store, CancellationToken ct) =>
{
    var agent = (await store.AgentsAsync(ct)).SingleOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
    if (string.IsNullOrWhiteSpace(input.Model) || input.Model.Length > 120 || input.MaxOutputTokens is < 100 or > 16000 || input.InputCostPerMillion <= 0 || input.OutputCostPerMillion <= 0) throw new ArgumentException("Configure a model, 100–16000 output tokens, and positive model prices.");
    if (input.ModelContextTokens <= input.MaxOutputTokens + 1024 || input.MaxInputTokens <= 0 || input.MaxInputTokens > input.ModelContextTokens - input.MaxOutputTokens) throw new ArgumentException("Input tokens must fit the model context window after output reservation.");
    var updated = agent with { Model = input.Model, Enabled = input.Enabled, MaxOutputTokens = input.MaxOutputTokens, InputCostPerMillion = input.InputCostPerMillion, OutputCostPerMillion = input.OutputCostPerMillion, ModelContextTokens = input.ModelContextTokens, MaxInputTokens = input.MaxInputTokens, SystemInstructions = input.SystemInstructions ?? agent.SystemInstructions, InstructionVersion = agent.InstructionVersion + 1 };
    await store.AppendAsync(new() { Kind = "instructionVersion", Name = id, Content = JsonSerializer.Serialize(agent) }, ct); await store.SaveAgentAsync(updated, ct); return updated;
});
api.MapGet("/projects/{id}/pull-requests", (string id, IPlatformStore store, CancellationToken ct) => Records("pullRequestStatus", id, null, null, 0, 0, 100, store, ct));
api.MapGet("/projects/{id}/reviews", (string id, IPlatformStore store, int? offset, int? limit, CancellationToken ct) => Records("review", id == "all" ? null : id, null, null, 0, offset, limit, store, ct));
api.MapGet("/reviews/{id}", async (string id, IPlatformStore store, CancellationToken ct) => await store.GetRecordAsync("review", id, ct) ?? throw new KeyNotFoundException());
api.MapGet("/approvals", (IPlatformStore store, int? offset, int? limit, CancellationToken ct) => Records("approval", null, null, null, 0, offset, limit, store, ct));
api.MapPost("/approvals/{id}/decision", async (string id, DecisionInput input, PlatformService service, CancellationToken ct) => { if (input.Reason is null || input.Reason.Length > 10000) throw new ArgumentException("Feedback is too long."); await service.DecideAsync(id, input.Approve, input.Reason, ct); return Results.Accepted(); });
api.MapPost("/integrations/github/repositories", async (RepositoryInput input, PlatformService service, ISourceControl source, CancellationToken ct) =>
{
    var project = await service.ProjectAsync(input.ProjectId, ct); GitHubSourceControl.Validate(input.Connection);
    var metadata = await source.ExecuteAsync(input.Connection, "ReadRepository", JsonSerializer.SerializeToElement(new { }), Guid.NewGuid().ToString("N"), ct);
    await service.SaveProjectAsync(project with { RepositoryConnection = input.Connection, DefaultBranch = input.Connection.DefaultBranch }, project.Version, ct); return Results.Ok(JsonSerializer.Deserialize<JsonElement>(metadata));
});
app.MapPost("/api/v1/webhooks/github", async (HttpContext ctx, ISourceControl source, IPlatformStore store, CancellationToken ct) =>
{
    if (ctx.Request.ContentLength > 1_000_000) return Results.StatusCode(413);
    using var buffer = new MemoryStream(); await ctx.Request.Body.CopyToAsync(buffer, ct); var bytes = buffer.ToArray();
    if (!source.VerifyWebhook(bytes, ctx.Request.Headers["X-Hub-Signature-256"].ToString())) return Results.Unauthorized();
    var delivery = ctx.Request.Headers["X-GitHub-Delivery"].ToString(); if (!Guid.TryParse(delivery, out _)) return Results.BadRequest();
    if (await store.GetRecordAsync("webhook", delivery, ct) != null) return Results.Ok(new { duplicate = true });
    using var payload = JsonDocument.Parse(bytes);
    var inserted = await store.IngestWebhookAsync(new() { Id = delivery, ExternalId = delivery, Kind = "webhook", Status = "Pending", Name = ctx.Request.Headers["X-GitHub-Event"].ToString(), Content = payload.RootElement.GetRawText() }, ct);
    return inserted ? Results.Accepted() : Results.Ok(new { duplicate = true });
}).RequireRateLimiting("api");
api.MapGet("/runs/{id}/stream", async (string id, string? kind, long? before, int? limit, IWorkspaceQueries queries, PlatformService service, CancellationToken ct) =>
{
    await service.RunAsync(id, ct);
    kind ??= "event";
    if (kind is not ("event" or "message" or "handoff" or "artifact" or "approval")) throw new ArgumentException("Unsupported activity kind.");
    return await queries.WindowAsync(kind, id, null, before, Math.Clamp(limit ?? 50, 1, 100), ct);
});
api.MapGet("/conversations/{id}/feed", async (string id, long? before, int? limit, IWorkspaceQueries queries, CancellationToken ct) => await queries.WindowAsync("message", null, id, before, Math.Clamp(limit ?? 30, 1, 100), ct));
api.MapGet("/runs/{id}/details/{kind}/{recordId}", async (string id, string kind, string recordId, IWorkspaceQueries queries, CancellationToken ct) =>
{
    if (kind is not ("event" or "tool" or "artifact" or "task" or "handoff" or "message")) throw new ArgumentException("Unsupported evidence kind.");
    return await queries.DetailsAsync(id, recordId, kind, ct);
});
api.MapPost("/runs/{id}/clarification", (string id, ClarificationInput input, PlatformService service, CancellationToken ct) => service.ClarifyAsync(id, input.Content, input.Version, ct));
api.MapGet("/reviews/feed", (long? before, int? limit, IWorkspaceQueries queries, CancellationToken ct) => queries.WindowAsync("review", null, null, before, Math.Clamp(limit ?? 30, 1, 100), ct));
api.MapGet("/approvals/feed", (long? before, int? limit, IWorkspaceQueries queries, CancellationToken ct) => queries.WindowAsync("approval", null, null, before, Math.Clamp(limit ?? 30, 1, 100), ct));
api.MapGet("/usage/ledger", (int? offset, int? limit, IWorkspaceQueries queries, CancellationToken ct) => queries.UsageAsync(Offset(offset), Math.Clamp(limit ?? 25, 1, 100), ct));
api.MapPatch("/agents/{id}/pricing", async (string id, PricingInput input, IPlatformStore store, CancellationToken ct) =>
{
    if (input.InputCostPerMillion <= 0 || input.OutputCostPerMillion <= 0) throw new ArgumentException("Model prices must be positive.");
    var agent = (await store.AgentsAsync(ct)).SingleOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
    await store.AppendAsync(new() { Kind = "instructionVersion", Name = id, Content = JsonSerializer.Serialize(agent) }, ct);
    var updated = agent with { InputCostPerMillion = input.InputCostPerMillion, OutputCostPerMillion = input.OutputCostPerMillion, InstructionVersion = agent.InstructionVersion + 1 };
    await store.SaveAgentAsync(updated, ct);
    return updated;
});
api.MapGet("/usage", (IPlatformStore store, CancellationToken ct) => store.StatisticsAsync(ct));
api.MapGet("/activity", (IPlatformStore store, CancellationToken ct) => store.RecentActivityAsync(ct));
api.MapGet("/settings", async (IExecutionSandbox sandbox, ISourceControl source, IConfiguration configuration, CancellationToken ct) => new { sandboxConfigured = sandbox.Configured, sandboxReady = await sandbox.IsReadyAsync(ct), sandboxProvider = configuration["EXECUTION_SANDBOX_PROVIDER"] ?? "Remote", githubConfigured = source.Configured, openAIConfigured = !string.IsNullOrWhiteSpace(configuration["OPENAI_API_KEY"]), authenticationMode = "Single owner", mergeRequiresHumanApproval = true });
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", async (IPlatformStore store, CancellationToken ct) => { try { return await store.PingAsync(ct) ? Results.Ok(new { status = "Healthy" }) : Results.StatusCode(503); } catch { return Results.StatusCode(503); } });
app.MapOpenApi().RequireAuthorization("Owner");
app.MapHub<EngineeringHub>("/hubs/engineering").RequireAuthorization("Owner");
app.Map("/api/{**path}", () => Results.Problem(statusCode: 404, title: "API route not found"));
app.Map("/hubs/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");
app.Run();

static int Limit(int? value) => Math.Clamp(value ?? 50, 1, 500);
static int Offset(int? value) => Math.Max(0, value ?? 0);
static void ValidateContent(string content) { if (string.IsNullOrWhiteSpace(content) || content.Length > 100000) throw new ArgumentException("Content must contain 1–100000 characters."); }
static async Task<Page<RecordEntry>> Records(string kind, string? projectId, string? runId, string? conversationId, long after, int? offset, int? limit, IPlatformStore store, CancellationToken ct) => new(await store.RecordsAsync(kind, projectId, runId, conversationId, after, Offset(offset), Limit(limit), ct), Offset(offset), Limit(limit));
public sealed record ProjectInput(string Name, string Description, string OriginalContext, string[] TechnologyStack, ExecutionPolicy? ExecutionPolicy, long Version = 0) { public Project ToProject() => new() { Name = Name, Description = Description, OriginalContext = OriginalContext, TechnologyStack = TechnologyStack, ExecutionPolicy = ExecutionPolicy ?? new() }; }
public sealed record LoginRequest(string Password);
public sealed record ContentInput(string Content);
public sealed record ClarificationInput(string Content, long Version);
public sealed record PricingInput(decimal InputCostPerMillion, decimal OutputCostPerMillion);
public sealed record TaskQueueInput(EngineeringTask[] Tasks);
public sealed record DecisionInput(bool Approve, string Reason);
public sealed record RequirementInput(string Title, string Description, string[] AcceptanceCriteria, int Priority = 1, string[]? Dependencies = null, string AssignedRole = "BackendEngineer");
public sealed record AgentInput(string Model, bool Enabled, int MaxOutputTokens, decimal InputCostPerMillion, decimal OutputCostPerMillion, string? SystemInstructions, int ModelContextTokens = 128000, int MaxInputTokens = 96000);
public sealed record RepositoryInput(string ProjectId, RepositoryConnection Connection);
public partial class Program { }

public sealed class ExecutionDefaults
{
    public int MaxIterations { get; set; } = 5; public int MaxToolCalls { get; set; } = 30; public int MaxDurationMinutes { get; set; } = 60; public decimal MaxBudgetUsd { get; set; } = 5;
    public ExecutionPolicy Policy() => new(MaxIterations, MaxToolCalls, MaxDurationMinutes, MaxBudgetUsd);
    public bool IsValid() { try { Policy().Validate(); return true; } catch (ArgumentException) { return false; } }
}
