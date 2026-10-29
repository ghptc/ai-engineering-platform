using AIEngineeringPlatform.Domain;
namespace AIEngineeringPlatform.Application;

/// <summary>Owner-configured ceilings, independent of run presets and extensions.</summary>
public sealed class ExecutionCapacity
{
    public int MaxToolCalls { get; set; } = 500;
    public int MaxIterations { get; set; } = 10;
    public int MaxRetries { get; set; } = 3;
    public int MaxDurationMinutes { get; set; } = 240;
    public decimal MaxBudgetUsd { get; set; } = 10;
    public void Validate(ExecutionPolicy policy)
    {
        policy.Validate();
        if (policy.MaxToolCalls > MaxToolCalls || policy.MaxIterations > MaxIterations || policy.MaxRetries > MaxRetries || policy.MaxDurationMinutes > MaxDurationMinutes || policy.MaxBudgetUsd > MaxBudgetUsd) throw new ArgumentException("Requested limits exceed owner-configured ExecutionCapacity ceilings.");
    }
}
