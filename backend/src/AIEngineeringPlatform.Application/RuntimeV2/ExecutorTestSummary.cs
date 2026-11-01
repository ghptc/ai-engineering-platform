namespace AIEngineeringPlatform.Application.RuntimeV2;

public sealed record ExecutorTestSummary(int ExecutedTests, int ExitCode)
{
    public static ExecutorTestSummary? Read(string output)
    {
        var marker = System.Text.RegularExpressions.Regex.Matches(output, @"(?m)^Executor evidence: tests=(\d+); exit=(-?\d+);").LastOrDefault();
        return marker?.Success == true && int.TryParse(marker.Groups[1].Value, out var count) && int.TryParse(marker.Groups[2].Value, out var exit)
            ? new(count, exit) : null;
    }
}
