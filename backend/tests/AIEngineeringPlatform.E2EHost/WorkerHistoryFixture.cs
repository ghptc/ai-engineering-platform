using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Application.RuntimeV2;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Integration.Tests;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;

// Deterministic browser data only. This assembly is never loaded by the production platform.
public static class WorkerHistoryFixture
{
    public static async Task SeedAsync(PlatformFactory factory, IPlatformStore store, CancellationToken ct)
    {
        var project = new Project { Id = "ux-worker-project", Name = "Independent worker history fixture", Description = "Deterministic UI fixtures; no live provider or source execution." };
        await store.SaveProjectAsync(project, null, ct);
        var run = await store.CreateRunAsync(new() { Id = "ux-worker-run", ProjectId = project.Id, ProjectSnapshot = project, AgentRuntimeVersion = 2, CurrentStage = RunStage.Paused, ResumeStage = RunStage.Implementing, Tasks = [new("Inspect independent worker history", "Inspect bounded deterministic task records", ["Keep operational output distinct from model proposals"])] }, ct);
        var connection = Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028";
        var rows = new MongoClient(connection).GetDatabase(factory.Database).GetCollection<BsonDocument>("agent_dispatch_v2");
        var frames = new PersistedRuntimeContextFrames(store);
        var definition = new AgentDefinition("BackendEngineer", AgentRole.BackendEngineer, "Backend Engineer", "Browser fixture", "test-only-model", "PRIVATE_CONTEXT_FIXTURE_DO_NOT_RENDER", [], ["ReadRepository"]);
        for (var i = 0; i < 55; i++)
        {
            var id = "ux-worker-task-" + i;
            var frame = await frames.SaveAsync(project.Id, run.Id, id, "PRIVATE_CONTEXT_FIXTURE_DO_NOT_RENDER", ct);
            var payload = new EngineeringTaskPayload(AgentRole.BackendEngineer, EngineeringTaskPurpose.Implementation, true, ["ReadRepository"], new(1, 2, 40000, 1000, 30, .1m), [], frame.Id);
            var message = new EngineeringMessage(1, EngineeringMessageKind.EngineeringTask, id, project.Id, run.Id, id, "Orchestrator", "BackendEngineer", null, "Deterministic task objective " + i, ["Fixture is an unverified model operation"], [], [], new("Implementing", 0, id), JsonSerializer.SerializeToElement(payload, EngineeringMessageContract.Json));
            EngineeringMessageContract.Parse(EngineeringMessageContract.Serialize(message), "Orchestrator", new HashSet<string> { "BackendEngineer" });
            var created = DateTimeOffset.Parse("2026-10-31T12:00:00Z").AddSeconds(i * 5);
            var result = new ModelExecutionResult(new("Deterministic model proposal; no verified source, tests or live provider call", "CONTINUE", [], [], [], [], "Inspect operational history"), "browser-fixture-response-" + i, 0, 0, 0);
            var task = new AgentDispatch(id, AgentRole.BackendEngineer, new(message, new(project, run, frame.Content, definition, id), frame.Id, frame.Sha256), "Completed", created, created.AddSeconds(30), "deterministic-browser-fixture", result, null, created.AddSeconds(1), created.AddSeconds(2));
            await rows.InsertOneAsync(new() { ["_id"] = id, ["ProjectId"] = project.Id, ["RunId"] = run.Id, ["Role"] = "BackendEngineer", ["Status"] = "Completed", ["Worker"] = task.Worker, ["CreatedAt"] = created.UtcDateTime, ["CreatedTicks"] = created.UtcTicks, ["Deadline"] = task.Deadline.UtcDateTime, ["Data"] = JsonSerializer.Serialize(task) }, cancellationToken: ct);
        }
    }
}
