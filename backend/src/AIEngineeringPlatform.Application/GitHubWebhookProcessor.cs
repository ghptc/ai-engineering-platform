using AIEngineeringPlatform.Domain;
using System.Text.Json;
namespace AIEngineeringPlatform.Application;

public sealed class GitHubWebhookProcessor(IPlatformStore store)
{
    public async Task ProcessPendingAsync(CancellationToken ct)
    {
        var receipts = await store.RecordsAsync("webhook", null, null, null, 0, 0, 20, ct, "Pending");
        foreach (var receipt in receipts)
        {
            try
            {
                using var document = JsonDocument.Parse(receipt.Content);
                var payload = document.RootElement;
                if (receipt.Name != "pull_request" || !payload.TryGetProperty("pull_request", out var pullRequest))
                {
                    await store.CompleteWebhookAsync(receipt.Id, "Ignored", ct);
                    continue;
                }
                var fullName = payload.GetProperty("repository").GetProperty("full_name").GetString();
                var installation = payload.GetProperty("installation").GetProperty("id").GetInt64();
                var number = payload.GetProperty("number").GetInt32();
                for (var offset = 0; ; offset += 100)
                {
                    var projects = await store.ProjectsAsync(offset, 100, ct);
                    foreach (var project in projects.Where(p => p.RepositoryConnection is { } connection && connection.InstallationId == installation && string.Equals(connection.Owner + "/" + connection.Repository, fullName, StringComparison.OrdinalIgnoreCase)))
                    {
                        var status = pullRequest.TryGetProperty("merged", out var merged) && merged.ValueKind == JsonValueKind.True ? "Merged" : pullRequest.GetProperty("state").GetString() ?? "unknown";
                        var id = "github-pr-" + receipt.Id + "-" + project.Id;
                        if (await store.GetRecordAsync("pullRequestStatus", id, ct) == null)
                            await store.AppendAsync(new RecordEntry { Id = id, Kind = "pullRequestStatus", ProjectId = project.Id, Name = "Pull request #" + number, Status = status, Content = pullRequest.GetRawText(), Metadata = new() { ["number"] = number.ToString(), ["deliveryId"] = receipt.Id, ["action"] = payload.GetProperty("action").GetString() ?? "" } }, ct);
                    }
                    if (projects.Count < 100) break;
                }
                await store.CompleteWebhookAsync(receipt.Id, "Processed", ct);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                await store.CompleteWebhookAsync(receipt.Id, "InvalidPayload", ct);
            }
        }
    }
}
