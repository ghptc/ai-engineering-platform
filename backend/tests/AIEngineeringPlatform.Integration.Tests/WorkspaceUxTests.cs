using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using AIEngineeringPlatform.Api;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
namespace AIEngineeringPlatform.Integration.Tests;
public class WorkspaceUxTests : IClassFixture<PlatformFactory>
{
    private readonly PlatformFactory factory;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public WorkspaceUxTests(PlatformFactory factory) => this.factory = factory;
    private async Task<HttpClient> LoginAsync()
    {
        await factory.Services.GetRequiredService<IPlatformStore>().InitializeAsync(default);
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { password = PlatformFactory.Password })).EnsureSuccessStatusCode(); return client;
    }
    [Fact]
    public async Task LargeEventHistoryIsLatestFirstPagedAndOriginalDetailsArePreserved()
    {
        using var client = await LoginAsync(); var store = factory.Services.GetRequiredService<IPlatformStore>();
        var project = new Project { Name = "UX history fixture" }; await store.SaveProjectAsync(project, null, default);
        var run = await store.CreateRunAsync(new() { ProjectId = project.Id, CurrentStage = RunStage.Paused, ResumeStage = RunStage.Implementing }, default);
        for (var i = 0; i < 1001; i++) await store.AppendAsync(new() { Id = run.Id + "-" + i, Kind = "event", Name = "Progress", ProjectId = project.Id, RunId = run.Id, Content = "Step " + i }, default);
        var huge = new RecordEntry { Kind = "event", Name = "ContextInventory", ProjectId = project.Id, RunId = run.Id, Content = JsonSerializer.Serialize(new { payload = new string('x', 100000) }) }; await store.AppendAsync(huge, default);
        var first = (await client.GetFromJsonAsync<RecordWindow>($"/api/v1/runs/{run.Id}/stream?limit=50", Json))!;
        Assert.Equal(50, first.Items.Count); Assert.True(first.HasMore); Assert.Equal(huge.Id, first.Items[0].Id);
        Assert.True(first.Items.Select(x=>x.Sequence).SequenceEqual(first.Items.Select(x=>x.Sequence).OrderDescending()));
        Assert.True(first.Items[0].Content.Length < 1000); Assert.Equal("true", first.Items[0].Metadata["payloadPreview"]);
        var next = (await client.GetFromJsonAsync<RecordWindow>($"/api/v1/runs/{run.Id}/stream?before={first.NextBefore}&limit=50", Json))!;
        Assert.Empty(first.Items.Select(x=>x.Id).Intersect(next.Items.Select(x=>x.Id)));
        var details = (await client.GetFromJsonAsync<WorkspaceDetails>($"/api/v1/runs/{run.Id}/details/event/{huge.Id}", Json))!;
        Assert.Equal(huge.Content, details.Entry.Content);
        // Exercise the real persisted hub replay across multiple 500-record batches.
        var delivered = new List<RecordEntry>();
        var caller = Substitute.For<ISingleClientProxy>();
        caller.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            delivered.Add((RecordEntry)call.ArgAt<object?[]>(1)[0]!);
            return Task.CompletedTask;
        });
        var clients = Substitute.For<IHubCallerClients>(); clients.Caller.Returns(caller);
        var context = Substitute.For<HubCallerContext>(); context.ConnectionId.Returns("ux-replay");
        var hub = new EngineeringHub(factory.Services.GetRequiredService<PlatformService>(), store, factory.Services.GetRequiredService<IWorkspaceQueries>())
        { Clients = clients, Context = context, Groups = Substitute.For<IGroupManager>() };
        var watermark = await hub.SubscribeProject(project.Id, 0);
        Assert.True(delivered.Count >= 1002);
        Assert.Equal(delivered.Count, delivered.Select(x => x.Id).Distinct().Count());
        Assert.Equal(delivered.Select(x => x.Sequence).Order(), delivered.Select(x => x.Sequence));
        Assert.Equal("true", delivered.Last().Metadata["payloadPreview"]);
        delivered.Clear();
        await store.AppendAsync(new() { Kind = "event", Name = "RecoveredAfterReconnect", ProjectId = project.Id, RunId = run.Id }, default);
        Assert.True(await hub.SubscribeProject(project.Id, watermark) > watermark);
        Assert.Equal("RecoveredAfterReconnect", Assert.Single(delivered).Name);
        delivered.Clear();
        Assert.True(await hub.SubscribeProject(project.Id, -1) > 0);
        Assert.Empty(delivered); // Cold snapshot refresh does not replay a project's entire lifetime.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/runs/another-run/details/event/{huge.Id}")).StatusCode);
    }
    [Fact]
    public async Task UsageLedgerUsesPersistedReceiptsAndPricingOnlyChangesFutureConfiguration()
    {
        using var client = await LoginAsync(); var store = factory.Services.GetRequiredService<IPlatformStore>();
        var project = new Project { Name = "Usage fixture" }; await store.SaveProjectAsync(project, null, default);
        var result = new ModelExecutionResult(new("Summary", "CONTINUE", [], [], [], [], ""), "receipt-ux", 100, 25, .002m);
        await store.AppendAsync(new() { Kind = "task", ProjectId = project.Id, RunId = "usage-run", Name = "Architect", Content = JsonSerializer.Serialize(result), Metadata = new() { ["model"] = "ux-model" } }, default);
        var ledger = (await client.GetFromJsonAsync<UsageLedger>("/api/v1/usage/ledger?limit=1", Json))!;
        Assert.Contains(ledger.Models, x=>x.Key=="ux-model" && x.InputTokens==100 && x.OutputTokens==25 && x.EstimatedCostUsd==.002m);
        Assert.Contains("confirmed billing is unavailable", ledger.CostBasis);
        var agent = (await store.AgentsAsync(default)).First();
        var response = await client.PatchAsJsonAsync($"/api/v1/agents/{agent.Id}/pricing", new { inputCostPerMillion=2, outputCostPerMillion=10 }); response.EnsureSuccessStatusCode();
        var updated = (await store.AgentsAsync(default)).Single(x=>x.Id==agent.Id);
        Assert.Equal(agent.Model, updated.Model); Assert.Equal(agent.SystemInstructions, updated.SystemInstructions); Assert.Equal(agent.AllowedTools, updated.AllowedTools); Assert.Equal(agent.MaxInputTokens, updated.MaxInputTokens);
        Assert.Equal(2, updated.InputCostPerMillion);
        var after = (await client.GetFromJsonAsync<UsageLedger>("/api/v1/usage/ledger", Json))!;
        Assert.Equal(ledger.Total.EstimatedCostUsd, after.Total.EstimatedCostUsd);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/v1/agents/{agent.Id}/pricing", new { inputCostPerMillion=0, outputCostPerMillion=10 })).StatusCode);
    }
    [Fact]
    public async Task ReadModelsAndPricingRemainOwnerProtected()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/usage/ledger")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/runs/test/stream")).StatusCode);
    }
}
