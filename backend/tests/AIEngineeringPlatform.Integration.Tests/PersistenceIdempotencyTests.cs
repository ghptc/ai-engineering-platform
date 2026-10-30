using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;

public sealed class PersistenceIdempotencyTests
{
    private static MongoPlatformStore Store() => new(Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028", "aep_test_" + Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task RepeatedExecutorFailureIgnoresNewAppendTimestampAndPreservesOriginalRecord()
    {
        var store = Store(); await store.InitializeAsync(default);
        var entry = new RecordEntry { Id = "executor-failure-operation", Kind = "event", Name = "ExecutorOperationBlocked", RunId = "run", ProjectId = "project", Status = "Blocked", Content = "Executor RunCommand failed (WorkspaceConflict, HTTP 409, operation operation).", Metadata = new() { ["operationId"] = "operation", ["httpStatus"] = "409" } };
        await store.AppendAsync(entry, default);
        var original = await store.GetRecordAsync("event", entry.Id, default);
        await store.AppendAsync(entry with { CreatedAt = entry.CreatedAt.AddMinutes(6) }, default);
        var replay = await store.GetRecordAsync("event", entry.Id, default);
        Assert.Equal(original!.CreatedAt, replay!.CreatedAt); Assert.Equal(original.Sequence, replay.Sequence);
        Assert.Equal(original.Content, replay.Content);
        Assert.Single(await store.RecordsAsync("event", "project", "run", null, 0, 0, 100, default));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ImmutableConflictsAreReportedWithoutOverwritingReceipts(bool otherKind)
    {
        var store = Store(); await store.InitializeAsync(default);
        var original = new RecordEntry { Id = "operation", Kind = "tool", Content = "successful receipt", Status = "Succeeded" };
        await store.AppendAsync(original, default);
        var error = await Assert.ThrowsAsync<PersistenceFailureException>(() => store.AppendAsync(original with { Kind = otherKind ? "event" : "tool", Content = "secret conflicting payload" }, default));
        Assert.Equal(11000, error.Code); Assert.Equal("_id_", error.Index); Assert.True(error.IdentityConflict);
        Assert.DoesNotContain("secret", error.Message);
        Assert.Equal(original.Content, (await store.GetRecordAsync("tool", original.Id, default))!.Content);
    }
    [Fact]
    public async Task ConcurrentReplayAndMetadataOrderingPreserveOneImmutableEvent()
    {
        var store = Store(); await store.InitializeAsync(default);
        var entry = new RecordEntry { Id = "stable", Kind = "event", Metadata = new() { ["a"] = "1", ["b"] = "2" } };
        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => store.AppendAsync(entry with { CreatedAt = entry.CreatedAt.AddSeconds(i), Metadata = new() { ["b"] = "2", ["a"] = "1" } }, default)));
        Assert.Single(await store.RecordsAsync("event", null, null, null, 0, 0, 100, default));
    }
    [Fact]
    public async Task ExternalIdConstraintIsNotMistakenForSameRecordReplay()
    {
        var store = Store(); await store.InitializeAsync(default);
        var entry = new RecordEntry { Id = "original", Kind = "webhook", ExternalId = "delivery" };
        await store.AppendAsync(entry, default);
        var error = await Assert.ThrowsAsync<PersistenceFailureException>(() => store.AppendAsync(entry with { Id = "other" }, default));
        Assert.Equal(11000, error.Code); Assert.Equal("ExternalId_1", error.Index);
        Assert.Null(await store.GetRecordAsync("webhook", "other", default));
    }

    [Fact]
    public async Task ConcurrentWebhookReplayReportsExactlyOneNewInsertion()
    {
        var store = Store(); await store.InitializeAsync(default);
        var entry = new RecordEntry { Id = "webhook", Kind = "webhook", ExternalId = "delivery", Content = "payload", Status = "Pending" };
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => store.IngestWebhookAsync(entry with { CreatedAt = entry.CreatedAt.AddSeconds(i) }, default)));
        Assert.Single(results, x => x);
    }

}
