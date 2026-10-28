namespace AIEngineeringPlatform.Application;

public static class ReviewMarkdown
{
    public static string Format(AgentResult review)
    {
        var lines = new List<string> { "## Engineering review", "", review.Summary, "", "Decision: **" + review.Decision + "**", "" };
        foreach (var finding in review.Findings ?? [])
        {
            // No inline position is published from a model claim. Source locations
            // require independent mapping to GitHub's current diff before inline use.
            lines.Add($"- **{finding.Severity}** — `{finding.File}`: {finding.Description}");
            lines.Add("  Suggested correction: " + finding.SuggestedCorrection);
        }
        if (review.Evidence.Length > 0) { lines.Add(""); lines.Add("Evidence: " + string.Join(", ", review.Evidence)); }
        return string.Join('\n', lines);
    }
}
