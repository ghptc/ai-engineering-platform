namespace AIEngineeringPlatform.Application;

public enum ExecutorFailureCategory { InvalidArguments, Authorization, RepositoryAccess, ProfileRestriction, WorkspaceConflict, Timeout, RateLimited, Infrastructure }

// Contains only locally authored messages and bounded protocol metadata, never remote response text.
public sealed class ExecutorFailureException(ExecutorFailureCategory category, string operationId, string operation, int? statusCode, bool outcomeUnknown)
    : Exception($"Executor {operation} failed ({category}, HTTP {statusCode?.ToString() ?? "unavailable"}, operation {operationId}). {Recovery(category, outcomeUnknown)}")
{
    public ExecutorFailureCategory Category { get; } = category;
    public string OperationId { get; } = operationId;
    public int? StatusCode { get; } = statusCode;
    public bool OutcomeUnknown { get; } = outcomeUnknown;
    private static string Recovery(ExecutorFailureCategory category, bool unknown) => (unknown ? "Reconcile the executor receipt and workspace before retrying; the operation may have completed. " : "") + (category switch
    {
        ExecutorFailureCategory.InvalidArguments => "Correct the bounded tool arguments.",
        ExecutorFailureCategory.Authorization => "Check executor authentication and operation permissions.",
        ExecutorFailureCategory.RepositoryAccess => "Check repository identity, installation access and allowlist.",
        ExecutorFailureCategory.ProfileRestriction => "Ask the administrator to configure a compatible fixed execution profile.",
        ExecutorFailureCategory.WorkspaceConflict => "Inspect workspace revision, operation identity and lease fencing; preserve existing files.",
        ExecutorFailureCategory.Timeout => "Check executor health and the configured execution timeout.",
        ExecutorFailureCategory.RateLimited => "Wait for executor capacity before resuming.",
        _ => "Check executor health and configuration before resuming."
    });
}
