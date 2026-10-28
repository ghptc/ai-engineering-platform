using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
namespace AIEngineeringPlatform.Api;

[Authorize]
public sealed class EngineeringHub(PlatformService service, IPlatformStore store) : Hub
{
    public async Task SubscribeProject(string projectId, long afterSequence = 0)
    {
        await service.ProjectAsync(projectId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, "project:" + projectId);
        var records = await store.RecordsAsync("event", projectId, null, null, afterSequence, 0, 500, Context.ConnectionAborted);
        foreach (var record in records) await Clients.Caller.SendAsync("EngineeringEvent", record, Context.ConnectionAborted);
    }
}
public sealed class SignalRPublisher(IHubContext<EngineeringHub> hub, IPlatformStore store, ILogger<SignalRPublisher> logger) : IExecutionPublisher
{
    public async Task PublishAsync(RecordEntry entry, CancellationToken ct)
    {
        var saved = await store.GetRecordAsync(entry.Kind, entry.Id, ct) ?? throw new InvalidOperationException("Cannot publish an unpersisted event.");
        try { await hub.Clients.Group("project:" + saved.ProjectId).SendAsync("EngineeringEvent", saved, ct); await hub.Clients.Group("project:" + saved.ProjectId).SendAsync(saved.Kind == "message" ? "AgentMessageCreated" : saved.Name, saved, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning("Live event delivery failed for persisted event {EventId}", saved.Id); }
    }
}
