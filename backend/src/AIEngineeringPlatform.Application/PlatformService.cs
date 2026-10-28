using AIEngineeringPlatform.Domain;
using System.Text.Json;
namespace AIEngineeringPlatform.Application;

public sealed class PlatformService(IPlatformStore store, IExecutionPublisher publisher)
{
    public async Task<ExecutionRun> StartTasksAsync(string projectId, string key, EngineeringTask[] tasks, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new ArgumentException("Idempotency-Key is required (maximum 128 characters).");
        var project = await ProjectAsync(projectId, ct);
        if (project.Status != "Active") throw new InvalidOperationException("Project is archived.");
        if (tasks is not { Length: > 0 } || tasks.Length > project.ExecutionPolicy.MaxIterations || tasks.Any(x => x == null)) throw new ArgumentException("Queue 1 task up to the existing execution iteration limit.");
        foreach (var task in tasks)
        {
            task.Validate();
            foreach (var id in task.RequirementIds ?? [])
                if ((await store.GetRecordAsync("requirement", id, ct))?.ProjectId != projectId) throw new ArgumentException("Task requirements must belong to this project.");
            foreach (var id in task.HumanInstructionIds ?? [])
            {
                var message = await store.GetRecordAsync("message", id, ct);
                if (message?.ProjectId != projectId || message.Name != "Client") throw new ArgumentException("Task instructions must reference this project's human messages.");
            }
        }
        return await store.CreateRunAsync(new() { ProjectId = projectId, IdempotencyKey = key, ProjectSnapshot = project, Policy = project.ExecutionPolicy, Tasks = tasks, RequirementIds = tasks[0].RequirementIds ?? [] }, ct);
    }
    public async Task<Project> ProjectAsync(string id, CancellationToken ct) => await store.GetProjectAsync(id, ct) ?? throw new KeyNotFoundException("Project not found.");
    public async Task<ExecutionRun> RunAsync(string id, CancellationToken ct) => await store.GetRunAsync(id, ct) ?? throw new KeyNotFoundException("Execution not found.");
    public async Task<Project> SaveProjectAsync(Project project, long? version, CancellationToken ct)
    {
        project.Validate();
        if (!await store.SaveProjectAsync(project with { Version = (version ?? -1) + 1, UpdatedAt = DateTimeOffset.UtcNow }, version, ct)) throw new ConcurrencyException();
        return await ProjectAsync(project.Id, ct);
    }
    public async Task<ExecutionRun> StartAsync(string projectId, string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new ArgumentException("Idempotency-Key is required (maximum 128 characters).");
        var project = await ProjectAsync(projectId, ct);
        if (project.Status != "Active") throw new InvalidOperationException("Project is archived.");
        var requirements = await store.RecordsAsync("requirement", projectId, null, null, 0, 0, 1000, ct);
        var superseded = requirements.Select(x => x.Metadata.GetValueOrDefault("supersedes")).Where(x => x != null).ToHashSet();
        return await store.CreateRunAsync(new() { ProjectId = projectId, IdempotencyKey = key, Policy = project.ExecutionPolicy, ProjectSnapshot = project, RequirementIds = requirements.Where(x => x.Status != "Verified" && !superseded.Contains(x.Id)).Select(x => x.Id).ToArray() }, ct);
    }
    public async Task<ExecutionRun> ControlAsync(string id, string action, CancellationToken ct)
    {
        var run = await RunAsync(id, ct);
        var target = action switch
        {
            "pause" => RunStage.Paused,
            "cancel" => RunStage.Cancelled,
            "resume" when run.CurrentStage == RunStage.Paused => run.ResumeStage,
            "retry" when run.CurrentStage == RunStage.Failed => run.ResumeStage,
            _ => throw new InvalidOperationException("Action is not available in the current state.")
        };
        var next = run.Transition(target);
        if (!await store.SaveRunAsync(next, run.Version, null, ct)) throw new ConcurrencyException();
        // The persistence boundary atomically commits the transition event to its outbox.
        return await RunAsync(id, ct);
    }
    public async Task<RecordEntry> EventAsync(ExecutionRun run, string name, string content, CancellationToken ct)
    {
        var entry = new RecordEntry { Kind = "event", ProjectId = run.ProjectId, RunId = run.Id, Name = name, Content = content };
        await store.AppendAsync(entry, ct);
        await publisher.PublishAsync(entry, ct);
        return entry;
    }
    public async Task<RecordEntry> MessageAsync(string conversationId, string sender, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > 100_000) throw new ArgumentException("Message must contain 1–100000 characters.");
        var conversation = await store.GetRecordAsync("conversation", conversationId, ct) ?? throw new KeyNotFoundException("Conversation not found.");
        var entry = new RecordEntry { Kind = "message", ProjectId = conversation.ProjectId, RunId = conversation.RunId, ConversationId = conversationId, Name = sender, Content = content };
        await store.AppendAsync(entry, ct);
        await publisher.PublishAsync(entry, ct);
        return entry;
    }
    public async Task DecideAsync(string id, bool approve, string reason, CancellationToken ct)
    {
        var approval = await store.GetRecordAsync("approval", id, ct) ?? throw new KeyNotFoundException("Approval not found.");
        if (approval.RunId is null) throw new InvalidOperationException("Approval has no execution.");
        var run = await RunAsync(approval.RunId, ct);
        if (run.CurrentStage != RunStage.AwaitingHumanApproval) throw new InvalidOperationException("Execution is not awaiting approval.");
        if (!await store.DecideApprovalAsync(id, approve, reason, ct)) throw new ConcurrencyException();
        // Worker reconciles the durable human decision. This survives a crash between the two writes.
        await EventAsync(run, "ApprovalDecided", approve ? "Approved by owner" : "Rejected by owner", ct);
    }
}
