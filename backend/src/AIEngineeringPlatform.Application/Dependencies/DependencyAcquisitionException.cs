namespace AIEngineeringPlatform.Application.Dependencies;

// Only bounded control-plane classifications cross the trust boundary. Never helper output or paths.
public sealed record DependencyFailure(string Phase, string Code, bool AcquisitionStarted, bool OutcomeKnown, string RecoveryAction);
public sealed class DependencyAcquisitionException(DependencyFailure failure, Exception? inner = null) : InvalidOperationException("Dependency acquisition failed: " + failure.Code, inner)
{
    public DependencyFailure Failure { get; } = failure;
}
