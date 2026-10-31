using AIEngineeringPlatform.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using NSubstitute;
using AIEngineeringPlatform.Api;
using AIEngineeringPlatform.Domain;
using Microsoft.Extensions.Logging.Abstractions;
namespace AIEngineeringPlatform.Integration.Tests;
public sealed class OperationalRelayTests
{
    [Fact] public async Task IndependentWorkerRecordsKeepGlobalOrderAcrossMessageKinds()
    {
        await using var factory = new PlatformFactory();
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var eventId = Guid.NewGuid().ToString("N"); var toolId = Guid.NewGuid().ToString("N"); var messageId = Guid.NewGuid().ToString("N");
        await store.AppendAsync(new() { Id = eventId, Kind = "event", Name = "Started", ProjectId = "relay-project" }, default);
        await store.AppendAsync(new() { Id = toolId, Kind = "tool", Name = "ReadFile", ProjectId = "relay-project" }, default);
        await store.AppendAsync(new() { Kind = "instructionVersion", Name = "Internal configuration" }, default);
        await store.AppendAsync(new() { Id = messageId, Kind = "message", Name = "BackendEngineer", ProjectId = "relay-project" }, default);
        var rows = await store.OperationalRecordsAsync(0, 100, false, default);
        Assert.Equal(new[] { eventId, toolId, messageId }, rows.Select(x => x.Id));
        Assert.Equal(rows.Select(x => x.Sequence).Order(), rows.Select(x => x.Sequence));
        Assert.Equal(messageId, Assert.Single(await store.OperationalRecordsAsync(0, 1, true, default)).Id);
        Assert.Equal(new[] { toolId, messageId }, (await store.OperationalRecordsAsync(rows[0].Sequence, 100, false, default)).Select(x => x.Id));
    }
    private sealed class Capture(IPlatformStore store) : IExecutionPublisher
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<RecordEntry> Rows = new();
        public readonly TaskCompletionSource Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PublishAsync(RecordEntry entry, CancellationToken ct)
        {
            Assert.NotNull(await store.GetRecordAsync(entry.Kind, entry.Id, ct));
            Rows.Enqueue(entry);
            if (Rows.Count >= 3) Completed.TrySetResult();
        }
    }
    [Fact] public async Task ApiRelayPublishesIndependentWorkerRecordsOnlyAfterPersistenceInGlobalOrder()
    {
        await using var factory = new PlatformFactory();
        var real = factory.Services.GetRequiredService<IPlatformStore>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = Substitute.For<IPlatformStore>();
        source.OperationalRecordsAsync(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var rows = await real.OperationalRecordsAsync(call.ArgAt<long>(0), call.ArgAt<int>(1), call.ArgAt<bool>(2), call.ArgAt<CancellationToken>(3));
            if (call.ArgAt<bool>(2)) ready.TrySetResult();
            return rows;
        });
        var capture = new Capture(real);
        using var relay = new PersistedEventRelay(source, capture, NullLogger<PersistedEventRelay>.Instance);
        await relay.StartAsync(default);
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var ids = new[] { Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N") };
            for (var i = 0; i < ids.Length; i++) await real.AppendAsync(new() { Id = ids[i], Kind = new[] { "event", "tool", "message" }[i], Name = "Persisted relay fixture", ProjectId = "relay-project" }, default);
            await capture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ids, capture.Rows.Where(row => ids.Contains(row.Id)).Select(row => row.Id));
        }
        finally { await relay.StopAsync(default); }
    }

}
