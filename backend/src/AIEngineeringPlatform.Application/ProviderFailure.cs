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
    long? InputTokens, long? OutputTokens, int Attempt, int? HttpStatus = null);

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
