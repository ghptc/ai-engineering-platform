using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIEngineeringPlatform.Domain;

namespace AIEngineeringPlatform.Certification;
internal static partial class Program
{
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly Uri Api = new("http://127.0.0.1:18099/api/v1/");
    private static async Task<HttpClient> LoginAsync(string passwordFile, CancellationToken ct)
    {
        var http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = Api, Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
        try { await SendAsync(http, HttpMethod.Post, "auth/login", new { password = (await File.ReadAllTextAsync(passwordFile, ct)).Trim() }, ct); return http; }
        catch { http.Dispose(); throw; }
    }
    internal static async Task<JsonElement> SendAsync(HttpClient http, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = JsonContent.Create(body, options: ApiJson);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Certification API {method} {path} returned HTTP {(int)response.StatusCode}. Reconcile persisted outcome before retrying any mutation.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(); var chunk = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > 8 * 1024 * 1024) throw new InvalidOperationException("Certification response exceeds bound.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.Length == 0 ? JsonSerializer.SerializeToElement(new { }) : JsonSerializer.Deserialize<JsonElement>(buffer.ToArray());
    }
    private static async Task StartAsync(IReadOnlyDictionary<string, string> settings, string output, string passwordFile, CancellationToken ct)
    {
        // CreateNew is a durable intent fence. Ambiguous HTTP results require reconciliation, never an automatic second run.
        var intent = Path.Combine(output, "start-intent.json");
        await using (var file = new FileStream(intent, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(file, new { repository = Owner + "/" + Repository, Installation, AuthorizedMaximumUsd, createdAt = DateTimeOffset.UtcNow }, ApiJson, ct);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(intent, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var fresh = Path.Combine(output, "start-preflight"); Directory.CreateDirectory(fresh);
        await PreflightAsync(settings, fresh, ct);
        using var http = await LoginAsync(passwordFile, ct);
        var runs = await SendAsync(http, HttpMethod.Get, "projects/all/runs?limit=1", null, ct);
        if (runs.GetProperty("items").GetArrayLength() != 0) throw new InvalidOperationException("Certification requires a fresh isolated database; existing executions will not be modified.");
        var definitions = await SendAsync(http, HttpMethod.Get, "agents", null, ct);
        foreach (var agent in definitions.EnumerateArray())
        {
            var role = Enum.Parse<AgentRole>(agent.GetProperty("role").GetString()!);
            if (role is AgentRole.SecurityEngineer or AgentRole.InfrastructureEngineer) continue;
            await SendAsync(http, HttpMethod.Patch, "agents/" + agent.GetProperty("id").GetString() + "/configuration", new
            {
                model = Required(settings, "OPENAI_DEFAULT_MODEL"), enabled = true, maxOutputTokens = 8000,
                // Conservative rates exceed standard and priority <=272k pricing; cached input is charged in full.
                inputCostPerMillion = 5m, outputCostPerMillion = 20m, modelContextTokens = 128000, maxInputTokens = 64000
            }, ct);
        }
        var policy = new ExecutionPolicy(MaxIterations: 3, MaxToolCalls: 100, MaxDurationMinutes: 60, MaxBudgetUsd: 9.50m, MaxRetries: 2);
        const string objective = "Build a small temperature converter as an independently agent-authored .NET 10 + React TypeScript Ant Design vertical slice. Plan the API and UI, implement both, create real executable tests, run the approved offline build and combined backend/frontend tests, perform QA and code review, then prepare a draft pull request at the owner approval gate. Preserve every existing repository file and branch, including README.md, feature.mjs and feature.test.mjs. Add the application under backend/ and frontend/. Use a new isolated engineering/{runId} branch from the current default HEAD; never select an existing feature branch. No merge or deployment. Use only the advertised controlled tools and approved dependency catalog; PrepareFrontendLock generates a missing compatible lock without networking. Do not invent test output. Inspect necessary facts once, then write concrete source files; reuse unchanged evidence.";
        string[] criteria = [
            "Working ASP.NET Core REST endpoint converts a finite Celsius input to Fahrenheit with JSON input/output; 0 maps to 32 and 100 to 212; malformed input returns a clear 400 response.",
            "Responsive accessible React TypeScript Ant Design UI calls that actual endpoint through a typed API client and shows loading, result and failure states; no fabricated API results.",
            "Executable backend tests exercise the real endpoint and invalid requests; executable frontend component tests exercise user input, API success and API failure.",
            "Approved backend and frontend builds and nonzero tests pass for the same final workspace revision; report actual receipt IDs, executed counts and failures honestly.",
            "QA maps acceptance criteria to real evidence and code review inspects the actual diff. Existing files and branches remain unchanged; delivery is a new draft PR only after owner approval."
        ];
        var project = await SendAsync(http, HttpMethod.Post, "projects", new { name = "M5 autonomous temperature converter", description = "Authorized disposable certification; AEP agents author all application source.", originalContext = objective, technologyStack = new[] { ".NET 10", "React", "TypeScript", "Ant Design" }, executionPolicy = policy }, ct);
        var projectId = project.GetProperty("id").GetString()!;
        await SaveAsync(Path.Combine(output, "project.json"), project, ct);
        await SendAsync(http, HttpMethod.Post, "integrations/github/repositories", new { projectId, connection = new RepositoryConnection(Owner, Repository, Installation, "main") }, ct);
        http.DefaultRequestHeaders.Add("Idempotency-Key", "m5-certification-" + projectId);
        var task = new EngineeringTask("Temperature converter vertical slice", objective, criteria, true, true, [AgentRole.BackendEngineer, AgentRole.FrontendEngineer]);
        var run = await SendAsync(http, HttpMethod.Post, "projects/" + projectId + "/tasks", new { tasks = new[] { task }, policy }, ct);
        await SaveAsync(Path.Combine(output, "run.json"), run, ct);
        Console.WriteLine("Started authorized V2 certification run " + run.GetProperty("id").GetString() + "; conservative cost ceiling USD 9.50; tool cap 100. No historical execution was changed.");
    }
    private static async Task ObserveAsync(string output, string passwordFile, CancellationToken ct)
    {
        using var http = await LoginAsync(passwordFile, ct);
        using var stored = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "run.json"), ct));
        var id = stored.RootElement.GetProperty("id").GetString()!;
        var run = await SendAsync(http, HttpMethod.Get, "runs/" + id, null, ct);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff");
        await SaveAsync(Path.Combine(output, "observed-" + stamp + ".json"), run, ct);
        foreach (var kind in new[] { "tool", "event", "artifact" })
        {
            var records = await SendAsync(http, HttpMethod.Get, "runs/" + id + "/stream?kind=" + kind + "&limit=100", null, ct);
            await SaveAsync(Path.Combine(output, kind + "-" + stamp + ".json"), records, ct);
        }
        Console.WriteLine($"Run {id}: stage={run.GetProperty("currentStage")}; tools={run.GetProperty("toolCalls")}; conservative cost={run.GetProperty("cost")}; failure={run.GetProperty("failureReason")}");
    }
}
