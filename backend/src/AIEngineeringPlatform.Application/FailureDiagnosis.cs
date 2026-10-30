using AIEngineeringPlatform.Domain;
namespace AIEngineeringPlatform.Application;

public static class FailureDiagnosis
{
    public static string Category(Exception error, RunStage stage)
    {
        if (error is ExecutorEvidenceException evidence) return "Executor" + evidence.Failure.Category;
        if (error is PersistenceFailureException) return "Persistence";
        if (error is ExecutorFailureException executor) return "Executor" + executor.Category;
        if (error is ProviderException provider) return "OpenAI" + provider.Diagnostics.ErrorCategory;
        if (error is ExecutionLimitException) return "ExecutionCapacity";
        if (error.Message.Contains("Protected context requires", StringComparison.Ordinal)) return "ContextBudget";
        if (error.Message.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) || error.Message.StartsWith("Model returned", StringComparison.Ordinal)) return "OpenAIProvider";
        if (error.Message.Contains("GitHub", StringComparison.OrdinalIgnoreCase) || error.Message.StartsWith("Push failed", StringComparison.Ordinal)) return "GitHubOperation";
        if (error.Message.Contains("executor", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("sandbox", StringComparison.OrdinalIgnoreCase)) return "LocalExecutor";
        return stage switch { RunStage.PreparingContext => "ContextBuilder", RunStage.RunningTests => "BuildTestProfile", RunStage.Validating => "QAEvidenceValidation", RunStage.Reviewing => "ReviewValidation", RunStage.PreparingDelivery => "GitHubDelivery", _ => "AgentExecution" };
    }
}
