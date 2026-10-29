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
    var project = new Project { Id = "ux-history-project", Name = "Engineering history fixture", Description = "**Product objective:** a focused JavaScript change.", OriginalContext = "# Product knowledge\n\n" + string.Concat(Enumerable.Repeat("Historical context stays behind disclosure.\n", 2000)) };
    await store.SaveProjectAsync(project, null, default);
    var run = new ExecutionRun { Id = "ux-history-run", ProjectId = project.Id, ProjectSnapshot = project, CurrentStage = RunStage.Paused, ResumeStage = RunStage.Implementing, Tasks = [new("Inspect a large engineering history", "Verify bounded activity and preserved evidence", ["Show only one bounded event window"]) ] };
    await store.CreateRunAsync(run, default);
    for (var i=0;i<1000;i++) await store.AppendAsync(new() { Id = "ux-history-event-"+i, Kind = "event", Name = i==999 ? "ContextInventory" : "ProgressRecorded", ProjectId = project.Id, RunId = run.Id, Content = i==999 ? System.Text.Json.JsonSerializer.Serialize(new { Source = "UX browser fixture", Large = new string('x',100000), End = "COMPLETE_CONTEXT_PAYLOAD" }) : "Persisted history item "+i }, default);
}
Console.WriteLine($"E2E fixture ready on http://localhost:{port}; adapters are deterministic test fixtures only.");
await Task.Delay(Timeout.InfiniteTimeSpan);
