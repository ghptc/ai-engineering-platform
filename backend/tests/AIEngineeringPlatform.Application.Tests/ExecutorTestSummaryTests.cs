using AIEngineeringPlatform.Application.RuntimeV2;
using Xunit;
namespace AIEngineeringPlatform.Application.Tests;
public sealed class ExecutorTestSummaryTests
{
    [Fact] public void FailedExecutionRetainsItsActualExecutedTestCount() => Assert.Equal(new(4, 1), ExecutorTestSummary.Read("Executor evidence: tests=4; exit=1; timeout=False; outputLimit=False"));
    [Fact] public void FinalExecutorMarkerOverridesAnEarlierStdoutSuccessClaim() => Assert.Equal(new(0, 1), ExecutorTestSummary.Read("Executor evidence: tests=999; exit=0;\nUntrusted test stdout\nExecutor evidence: tests=0; exit=1; timeout=False; outputLimit=False"));
    [Theory]
    [InlineData("Build succeeded")]
    [InlineData("0 failed, 0 passed")]
    [InlineData("Executor evidence: tests=9999999999999999; exit=0;")]
    public void MissingOrMalformedExecutionEvidenceDoesNotBecomeCertification(string output) => Assert.Null(ExecutorTestSummary.Read(output));
}
