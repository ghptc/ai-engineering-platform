using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using System.Text.Json;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;

public sealed class WebhookTests
{
    [Fact]
    public async Task PullRequestReceiptsAreDeduplicatedAndMatchInstalledRepository()
    {
        var store = new MongoPlatformStore(Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028", "aep_webhook_" + Guid.NewGuid().ToString("N"));
        await store.InitializeAsync(default);
        var project = new Project { Name = "Webhook project", RepositoryConnection = new("test-owner", "test-repo", 123) };
        await store.SaveProjectAsync(project, null, default);
        var receipt = new RecordEntry { Id = Guid.NewGuid().ToString(), Kind = "webhook", Name = "pull_request", Status = "Pending", ExternalId = Guid.NewGuid().ToString(), Content = JsonSerializer.Serialize(new { action = "closed", number = 7, repository = new { full_name = "test-owner/test-repo" }, installation = new { id = 123 }, pull_request = new { state = "closed", merged = true } }) };
        Assert.True(await store.IngestWebhookAsync(receipt, default));
        Assert.False(await store.IngestWebhookAsync(receipt with { CreatedAt = DateTimeOffset.UtcNow }, default));
        var processor = new GitHubWebhookProcessor(store); await processor.ProcessPendingAsync(default); await processor.ProcessPendingAsync(default);
        var updates = await store.RecordsAsync("pullRequestStatus", project.Id, null, null, 0, 0, 100, default);
        Assert.Single(updates); Assert.Equal("Merged", updates[0].Status);
        Assert.Equal("Processed", (await store.GetRecordAsync("webhook", receipt.Id, default))!.Status);
    }
}
