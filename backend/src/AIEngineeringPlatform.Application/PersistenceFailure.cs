namespace AIEngineeringPlatform.Application;

// Driver messages may contain document contents. Retain only protocol codes and locally authored labels.
public sealed class PersistenceFailureException(int code, string index, string recordId, string recordKind, bool identityConflict)
    : Exception($"Record append failed (MongoDB code {code}, index {index}, {(identityConflict ? "conflicting immutable record" : "write rejected")}). Existing records were preserved.")
{
    public int Code { get; } = code;
    public string Index { get; } = index;
    public string RecordId { get; } = recordId;
    public string RecordKind { get; } = recordKind;
    public bool IdentityConflict { get; } = identityConflict;
}

public sealed class ExecutorEvidenceException(ExecutorFailureException failure, Exception evidenceFailure)
    : Exception(failure.Message + " Failure evidence could not be appended: " + (evidenceFailure is PersistenceFailureException persistence ? persistence.Message : "persistence service unavailable. Existing records were preserved."))
{
    public ExecutorFailureException Failure { get; } = failure;
    public PersistenceFailureException? PersistenceFailure { get; } = evidenceFailure as PersistenceFailureException;
}
