using AIEngineeringPlatform.Domain;
using System.Text.Json;
namespace AIEngineeringPlatform.Application;

public sealed class ConversationResponder(IPlatformStore store, PlatformService service, ILanguageModelProvider provider, IExecutionPublisher publisher)
{
    private readonly SemaphoreSlim replyGate = new(1, 1);
    public async Task<RecordEntry> ReplyAsync(string conversationId, CancellationToken ct)
    {
        await replyGate.WaitAsync(ct);
        try { return await ReplyCoreAsync(conversationId, ct); }
        finally { replyGate.Release(); }
    }
    private async Task<RecordEntry> ReplyCoreAsync(string conversationId, CancellationToken ct)
    {
        var conversation = await store.GetRecordAsync("conversation", conversationId, ct) ?? throw new KeyNotFoundException("Conversation not found.");
        if (conversation.Name != "ClientConversation") throw new InvalidOperationException("Only client conversations can request an analyst response.");
        var project = await service.ProjectAsync(conversation.ProjectId, ct);
        var messages = await store.RecordsAsync("message", project.Id, null, conversationId, 0, 0, 500, ct);
        var last = messages.LastOrDefault() ?? throw new InvalidOperationException("Send a message before requesting a response.");
        if (last.Name != "Client") return last;
        var responseId = "reply-" + last.Id;
        var existing = await store.GetRecordAsync("message", responseId, ct);
        if (existing != null) return existing;
        var definition = (await store.AgentsAsync(ct)).Single(x => x.Role == AgentRole.BusinessAnalyst) with { AllowedTools = [] };
        if (!definition.Enabled) throw new InvalidOperationException("The Business Analyst is disabled.");
        var usage = await store.RecordsAsync("usage", project.Id, null, conversationId, 0, 0, 1000, ct);
        var spent = usage.Sum(x => JsonSerializer.Deserialize<ModelExecutionResult>(x.Content)!.Cost);
        var run = new ExecutionRun { ProjectId = project.Id, Policy = project.ExecutionPolicy, Cost = spent };
        run.CheckLimits(DateTimeOffset.UtcNow);
        var context = JsonSerializer.Serialize(new { project.Name, project.OriginalContext, History = messages.TakeLast(50), Request = "Respond to the client's latest message. Clarify open business decisions and describe assumptions. You have no repository tools for this conversation. Engineering claims require a separate verified execution." });
        var result = await provider.ExecuteAsync(new(project, run, context, definition), ct);
        if (result.Output.ToolCalls.Length > 0 || string.IsNullOrWhiteSpace(result.Output.Summary)) throw new InvalidOperationException("Conversation response requested unavailable tools or has no message.");
        await store.AppendAsync(new() { Id = "usage-" + responseId, Kind = "usage", ProjectId = project.Id, ConversationId = conversationId, Name = definition.Model, Content = JsonSerializer.Serialize(result) }, ct);
        var entry = new RecordEntry { Id = responseId, Kind = "message", ProjectId = project.Id, ConversationId = conversationId, Name = "BusinessAnalyst", Content = JsonSerializer.Serialize(result.Output), Metadata = new() { ["responseId"] = result.ResponseId, ["inReplyTo"] = last.Id, ["scope"] = "ConversationOnly" } };
        await store.AppendAsync(entry, ct);
        await publisher.PublishAsync(entry, ct);
        return entry;
    }
}
