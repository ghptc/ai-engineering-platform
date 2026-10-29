using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
namespace AIEngineeringPlatform.Api;

[Authorize]
public sealed class EngineeringHub(PlatformService service, IPlatformStore store, IWorkspaceQueries queries) : Hub
{
    public async Task<long> SubscribeProject(string projectId, long afterSequence = 0)
    {
        await service.ProjectAsync(projectId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, "project:" + projectId);
        if (afterSequence < 0)
        {
            var latest = await queries.WindowAsync("event", null, null, null, 1, Context.ConnectionAborted);
            return latest.Items.FirstOrDefault()?.Sequence ?? 0;
        }
        var cursor = afterSequence;
        while (true)
        {
            var records = await store.RecordsAsync("event", projectId, null, null, cursor, 0, 500, Context.ConnectionAborted);
            foreach (var record in records) await Clients.Caller.SendAsync("EngineeringEvent", RecordPresentation.Preview(record with { Content = AIEngineeringPlatform.Infrastructure.SecretRedactor.Redact(record.Content) }), Context.ConnectionAborted);
            if (records.Count > 0) cursor = records[^1].Sequence;
            if (records.Count < 500) return cursor;
        }
    }
}
public sealed class SignalRPublisher(IHubContext<EngineeringHub> hub, IPlatformStore store, ILogger<SignalRPublisher> logger) : IExecutionPublisher
{
    public async Task PublishAsync(RecordEntry entry, CancellationToken ct)
    {
        var saved = await store.GetRecordAsync(entry.Kind, entry.Id, ct) ?? throw new InvalidOperationException("Cannot publish an unpersisted event.");
        try { await hub.Clients.Group("project:" + saved.ProjectId).SendAsync("EngineeringEvent", RecordPresentation.Preview(saved with { Content = AIEngineeringPlatform.Infrastructure.SecretRedactor.Redact(saved.Content) }), ct); await hub.Clients.Group("project:" + saved.ProjectId).SendAsync(saved.Kind == "message" ? "AgentMessageCreated" : saved.Name, RecordPresentation.Preview(saved with { Content = AIEngineeringPlatform.Infrastructure.SecretRedactor.Redact(saved.Content) }), ct); }
        catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning("Live event delivery failed for persisted event {EventId}", saved.Id); }
    }
}
