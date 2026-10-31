using AIEngineeringPlatform.Integration.Tests;
using Microsoft.Extensions.DependencyInjection;
using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
var contentRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/AIEngineeringPlatform.Api"));
Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_AIENGINEERINGPLATFORM_API", contentRoot);
Environment.SetEnvironmentVariable("ASPNETCORE_CONTENTROOT", contentRoot);
await using var factory = new PlatformFactory();
var port = int.Parse(Environment.GetEnvironmentVariable("E2E_API_PORT") ?? "5088");
factory.UseKestrel(port);
using var client = factory.CreateClient();
var store = factory.Services.GetRequiredService<IPlatformStore>();
for (var attempt = 0; attempt < 30; attempt++) { try { await store.InitializeAsync(default); break; } catch when (attempt < 29) { await Task.Delay(1000); } }
if (Environment.GetEnvironmentVariable("E2E_SEED_UX_HISTORY") == "1")
{
    await WorkerHistoryFixture.SeedAsync(factory, store, default);
    var project = new Project { Id = "ux-history-project", Name = "Engineering history fixture", Description = "**Product objective:** a focused JavaScript change.", OriginalContext = "# Product knowledge\n\n" + string.Concat(Enumerable.Repeat("Historical context stays behind disclosure.\n", 2000)) };
    await store.SaveProjectAsync(project, null, default);
    var run = new ExecutionRun { Id = "ux-history-run", ProjectId = project.Id, ProjectSnapshot = project, CurrentStage = RunStage.Paused, ResumeStage = RunStage.Implementing, Tasks = [new("Inspect a large engineering history", "Verify bounded activity and preserved evidence", ["Show only one bounded event window"]) ] };
    await store.CreateRunAsync(run, default);
    for (var i=0;i<1000;i++) await store.AppendAsync(new() { Id = "ux-history-event-"+i, Kind = "event", Name = i==999 ? "ContextInventory" : "ProgressRecorded", ProjectId = project.Id, RunId = run.Id, Content = i==999 ? System.Text.Json.JsonSerializer.Serialize(new { Source = "UX browser fixture", Large = new string('x',100000), End = "COMPLETE_CONTEXT_PAYLOAD" }) : "Persisted history item "+i }, default);
    // Read-only incident excerpt, loaded exclusively by this test assembly into its isolated DB.
    var fixturePath = Path.GetFullPath(Path.Combine(contentRoot, "../../../frontend/tests/fixtures/devpulse-persisted.json"));
    using var fixture = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
    var fixtureJson = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
    fixtureJson.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    var incident = System.Text.Json.JsonSerializer.Deserialize<ExecutionRun>(fixture.RootElement.GetProperty("run"), fixtureJson)!;
    var incidentProject = new Project { Id = incident.ProjectId, Name = "DevPulse · persisted incident fixture", Description = "Read-only sanitized operational excerpt. No live execution or credentials.", OriginalContext = "Browser verification of actual persisted operational records." };
    await store.SaveProjectAsync(incidentProject, null, default);
    await store.CreateRunAsync(incident with { ProjectSnapshot = incidentProject }, default);
    var records = new[] { "events", "tools", "handoffs" }.SelectMany(kind => fixture.RootElement.GetProperty(kind).EnumerateArray().Select(e => System.Text.Json.JsonSerializer.Deserialize<RecordEntry>(e, fixtureJson)!)).OrderBy(e => e.Sequence);
    foreach (var record in records) await store.AppendAsync(record, default);
    // Deterministic rework/retry contract fixture, distinct from the real incident excerpt.
    var reworkProject = new Project { Id = "ux-rework-project", Name = "Rework and retry contract fixture", Description = "Deterministic browser fixture; no live execution." };
    await store.SaveProjectAsync(reworkProject, null, default);
    var reworkSeed = new ExecutionRun { Id = "ux-rework-run", IdempotencyKey = "ui-rework-fixture", ProjectId = reworkProject.Id, ProjectSnapshot = reworkProject, CurrentStage = RunStage.Validating, Tasks = [new("Rework and retry UI fixture", "Deterministic UI contract fixture; no live delivery or model calls.", ["Return rejected QA to the responsible frontend engineer"], RequiresBusinessAnalysis: false, RequiresArchitecture: false, Developers: [AgentRole.BackendEngineer, AgentRole.FrontendEngineer])] };
    var retried = reworkSeed.Transition(RunStage.Failed, "Deterministic validation failure").Transition(RunStage.Validating);
    var rework = ReworkPlanner.Create(retried, AgentRole.QAEngineer, new("Deterministic fixture: inaccessible control", "REQUEST_CHANGES", [], [], [], [], "", ReworkTargets: ["FrontendEngineer"]), "ui-fixture-revision");
    var pausedRework = (retried with { ReworkPlan = rework }).Transition(RunStage.Implementing).Transition(RunStage.Paused, "Deterministic fixture paused for inspection; no live execution.");
    await store.CreateRunAsync(pausedRework, default);
    var transitions = new[] { "RunningTests → Validating", "Validating → Failed", "Failed → Validating", "Validating → Implementing", "Implementing → Paused", "Paused → Implementing", "Implementing → Paused" };
    var seconds = new[] { 0, 5, 6, 10, 12, 72, 75 };
    for (var i = 0; i < transitions.Length; i++) await store.AppendAsync(new() { Id = "ux-retry-event-" + i, Kind = "event", Name = "StageChanged", ProjectId = reworkProject.Id, RunId = pausedRework.Id, Content = transitions[i], CreatedAt = DateTimeOffset.Parse("2026-10-30T12:00:00Z").AddSeconds(seconds[i]) }, default);
}
Console.WriteLine($"E2E fixture ready on http://localhost:{port}; adapters are deterministic test fixtures only.");
await Task.Delay(Timeout.InfiniteTimeSpan);
