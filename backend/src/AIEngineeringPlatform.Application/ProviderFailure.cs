namespace AIEngineeringPlatform.Application;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ProviderErrorCategory>))]
public enum ProviderErrorCategory
{
    CallerCancellation, MalformedJson, InvalidToolArguments, InvalidSchema, EmptyOutput, UnexpectedContent,
    IncompleteResponse, ResponseFailed, RequestTimeout, SdkTimeout, TransportFailure, HttpFailure, MissingUsage, RunDeadline
}

// Deliberately contains no model output, request body, credential or exception message.
public sealed record ProviderDiagnostics(string Model, string? ResponseId, string? RequestId,
    string? ResponseStatus, string? IncompleteReason, ProviderErrorCategory? ErrorCategory,
    long? InputTokens, long? OutputTokens, int Attempt, int? HttpStatus = null, int? MaxOutputTokens = null)
{
    public string? IncompleteClassification => ResponseStatus != "incomplete" ? null : IncompleteReason switch
    {
        "max_output_tokens" => "OutputLimit",
        "content_filter" => "ContentFilter",
        "steered" => "Steered",
        _ => "Unknown"
    };
    public string? RecoveryGuidance => IncompleteClassification switch
    {
        "OutputLimit" => "Retry the failed checkpoint with a larger configured output allowance or a smaller focused plan. Recovery requires remaining budget, context capacity and run time; extend limits explicitly if exhausted.",
        "ContentFilter" => "Review the request for policy issues before retrying. Increasing output tokens will not address this reason.",
        "Steered" => "Inspect the interrupted request before retrying the checkpoint.",
        "Unknown" => "The provider did not supply a recognized incomplete reason. Inspect diagnostics before retrying; do not assume truncation.",
        _ => null
    };
}

public sealed class ProviderException(ProviderDiagnostics diagnostics) : InvalidOperationException(
    $"OpenAI provider failed: {diagnostics.ErrorCategory}. No tools from this response were executed. Inspect provider diagnostics before resuming.")
{
    public ProviderDiagnostics Diagnostics { get; } = diagnostics;
}

public sealed record ProviderCharge(ProviderDiagnostics Diagnostics, decimal Cost);
public static class ProviderAccounting
{
    public static async Task<ProviderCharge[]> ChargesAsync(IPlatformStore store, string runId, CancellationToken ct)
    {
        var entries = new List<AIEngineeringPlatform.Domain.RecordEntry>();
        for (var offset = 0; ; offset += 1000)
        {
            var page = await store.RecordsAsync("providerRequest", null, runId, null, 0, offset, 1000, ct);
            entries.AddRange(page);
            if (page.Count < 1000) break;
        }
        return entries.GroupBy(x => x.Metadata["requestKey"]).Select(g => System.Text.Json.JsonSerializer.Deserialize<ProviderCharge>(g.OrderBy(x => x.Status == "Reserved" ? 0 : 1).Last().Content)!).ToArray();
    }
}
