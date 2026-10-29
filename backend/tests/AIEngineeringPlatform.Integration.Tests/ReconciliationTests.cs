using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;

public sealed class ReconciliationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task NoOpRequiresCurrentTestsAndQaAndReusesExistingDelivery(bool existingPr, bool revisionMismatch)
    {
        await using var root = new PlatformFactory();
        var sandbox = new DeterministicSandbox(true, existingPr, revisionMismatch);
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(new DeterministicModel(true));
            services.RemoveAll<IExecutionSandbox>(); services.AddSingleton<IExecutionSandbox>(sandbox);
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { password = PlatformFactory.Password })).EnsureSuccessStatusCode();
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        var project = new Project { Name = "Already completed task", RepositoryConnection = new("owner", "fixture", 1) };
        await store.SaveProjectAsync(project, null, default);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/tasks", new { tasks = new[] { new EngineeringTask("Answer", "Verify answer returns 2", ["answer() is numeric 2", "node tests pass"]) } });
        response.EnsureSuccessStatusCode();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var run = (await response.Content.ReadFromJsonAsync<ExecutionRun>(json))!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        while (run.IsRunnable) { await Task.Delay(100, timeout.Token); run = (await store.GetRunAsync(run.Id, timeout.Token))!; }
        Assert.Equal(revisionMismatch ? RunStage.Failed : RunStage.Completed, run.CurrentStage);
        Assert.Equal(revisionMismatch ? ExecutionOutcome.Failed : ExecutionOutcome.AlreadySatisfied, run.Outcome);
        Assert.Contains("RunTests", sandbox.Operations);
        Assert.DoesNotContain("WriteFile", sandbox.Operations);
        Assert.DoesNotContain("CreateCommit", sandbox.Operations);
        Assert.DoesNotContain("PushBranch", sandbox.Operations);
        var tasks = await store.RecordsAsync("task", project.Id, run.Id, null, 0, 0, 100, default);
        Assert.DoesNotContain(tasks, x => x.Name is "BusinessAnalyst" or "Architect" or "TestEngineer");
        Assert.Contains(tasks, x => x.Name == "QAEngineer");
        var tools = await store.RecordsAsync("tool", project.Id, run.Id, null, 0, 0, 100, default);
        Assert.DoesNotContain(tools, x => x.Name == "CreatePullRequest");
        if (existingPr && !revisionMismatch)
        {
            var artifacts = await store.RecordsAsync("artifact", project.Id, run.Id, null, 0, 0, 100, default);
            Assert.Contains(artifacts, x => x.Name == "PullRequest" && x.Content.Contains("example.invalid/pr/7"));
        }
    }
}
