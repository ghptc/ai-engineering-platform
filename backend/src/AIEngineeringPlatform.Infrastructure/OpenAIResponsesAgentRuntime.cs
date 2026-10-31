using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Application.RuntimeV2;

namespace AIEngineeringPlatform.Infrastructure;

// The official Responses provider remains the only model transport. Tools stay in the orchestrator.
public sealed class OpenAIResponsesAgentRuntime(ILanguageModelProvider provider, IRuntimeContextFrames frames, IRuntimeInvocationStore checkpoints) : IAgentRuntime
{
    public string Name => "OpenAIResponsesAgentRuntime";
    public async Task<AgentRuntimeResult> ExecuteAsync(AgentRuntimeRequest request, CancellationToken ct)
    {
        var message = EngineeringMessageContract.Parse(EngineeringMessageContract.Serialize(request.Task), "Orchestrator", new HashSet<string> { request.Context.Definition.Role.ToString() });
        if (message.Kind != EngineeringMessageKind.EngineeringTask || message.RunId != request.Context.Run.Id || message.ProjectId != request.Context.Project.Id || request.Context.Run.ProjectId != request.Context.Project.Id) throw new UnauthorizedAccessException("Agent runtime task scope mismatch.");
        var task = EngineeringMessageContract.Payload<EngineeringTaskPayload>(message);
        var definition = request.Context.Definition;
        if (!definition.Enabled || task.Role != definition.Role || task.ContextFrameId != request.ContextFrameId || task.AuthorizedTools.Any(x => !definition.AllowedTools.Contains(x)) || task.Budget.MaxToolCalls > request.Context.Run.Policy.MaxToolCalls || task.Budget.MaxInputTokens > definition.MaxInputTokens || task.Budget.MaxOutputTokens > definition.MaxOutputTokens || task.Budget.MaxCostUsd > request.Context.Run.Policy.MaxBudgetUsd) throw new UnauthorizedAccessException("Agent runtime task exceeds its authorized role or budget.");
        var frame = await frames.GetAsync(request.ContextFrameId, message.ProjectId, message.RunId, ct);
        if (frame.TaskId != message.TaskId || frame.Sha256 != request.ContextSha256 || frame.Content != request.Context.Context) throw new InvalidOperationException("Agent runtime context frame does not match its persisted input.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(task.Budget.TimeoutSeconds));
        var context = request.Context with
        {
            Context = frame.Content, InvocationId = message.TaskId,
            Definition = definition with { AllowedTools = task.AuthorizedTools, MaxInputTokens = task.Budget.MaxInputTokens, MaxOutputTokens = task.Budget.MaxOutputTokens },
            RuntimeLimits = new(task.Budget.MaxModelRequests, task.Budget.MaxCostUsd)
        };
        var fingerprint = PersistedRuntimeContextFrames.Hash(EngineeringMessageContract.Serialize(message) + frame.Sha256 + definition.Model + definition.InstructionVersion + definition.SystemInstructions);
        var begun = await checkpoints.BeginAsync(new(message.MessageId, fingerprint, frame.Id, "Running", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(task.Budget.TimeoutSeconds), Guid.NewGuid().ToString("N"), null, null), deadline.Token);
        if (!begun.Acquired)
        {
            var saved = begun.Checkpoint;
            while (saved.Status == "Running" && saved.Deadline > DateTimeOffset.UtcNow)
            {
                await Task.Delay(100, deadline.Token);
                saved = await checkpoints.GetAsync(message.MessageId, deadline.Token);
            }
            if (saved.Status == "Completed" && saved.Result != null) return new(Name, message.TaskId, frame.Id, saved.Result);
            throw new RuntimeReconciliationException("The original runtime invocation failed or has an unknown outcome. Reconcile its checkpoint and provider receipts before an explicit bounded retry.");
        }
        try
        {
            if (context.Run.Cost >= context.Run.Policy.MaxBudgetUsd || DateTimeOffset.UtcNow >= context.Run.StartedAt.AddMinutes(context.Run.Policy.MaxDurationMinutes)) throw new AIEngineeringPlatform.Domain.ExecutionLimitException("No remaining runtime request allowance; cached outcomes remain available for reconciliation.");
            var response = await provider.ExecuteAsync(context, deadline.Token).WaitAsync(deadline.Token);
            AgentOutputContract.Validate(context, response);
            if (string.IsNullOrWhiteSpace(response.ResponseId) || response.InputTokens < 0 || response.OutputTokens < 0 || response.Cost < 0) throw new InvalidOperationException("Invalid runtime provider receipt.");
            if (response.Output.ToolCalls.Length > task.Budget.MaxToolCalls || response.Output.ToolCalls.Any(x => !task.AuthorizedTools.Contains(x.Name)) || response.Cost > task.Budget.MaxCostUsd) throw new InvalidOperationException("Agent runtime output exceeds its authorized contract.");
            using var persist = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await checkpoints.CompleteAsync(message.MessageId, begun.Checkpoint.Owner, response, persist.Token);
            return new(Name, message.TaskId, frame.Id, response);
        }
        catch (Exception error)
        {
            using var persist = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await checkpoints.FailAsync(message.MessageId, begun.Checkpoint.Owner, error.GetType().Name, persist.Token);
            throw;
        }
    }
}
