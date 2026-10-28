using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;

public sealed class LiveAgentFactAttribute : FactAttribute
{
    public LiveAgentFactAttribute() { if (Environment.GetEnvironmentVariable("RUN_LIVE_AGENT_TESTS") != "1") Skip = "Opt-in real OpenAI/GitHub/local-executor cycle; requires an explicitly authorized disposable aep-sandbox-* repository and spending limits."; }
}
public sealed class LiveAgentCycleTests
{
    [LiveAgentFact]
    public async Task RealOpenAIProducesStructuredOutputAndUsage()
    {
        string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new InvalidOperationException("Live verification requires " + name);
        var inputPrice = decimal.Parse(Required("LIVE_MODEL_INPUT_PRICE"), System.Globalization.CultureInfo.InvariantCulture);
        var outputPrice = decimal.Parse(Required("LIVE_MODEL_OUTPUT_PRICE"), System.Globalization.CultureInfo.InvariantCulture);
        var definition = new AgentDefinition("live-probe", AgentRole.BackendEngineer, "Live probe", "Structured adapter verification", Required("OPENAI_DEFAULT_MODEL"), "This is an API contract probe. Return summary 'Live structured response verified', decision CONTINUE, no tool calls and empty arrays. Do not claim any code was changed or tests executed.", [], [], MaxOutputTokens: 800, InputCostPerMillion: inputPrice, OutputCostPerMillion: outputPrice);
        var run = new ExecutionRun { Policy = new(MaxBudgetUsd: 0.10m) };
        var result = await new OpenAIProvider(Required("OPENAI_API_KEY")).ExecuteAsync(new(new Project { Name = "API contract probe" }, run, "Verify the structured output contract only.", definition), default);
        Assert.StartsWith("resp_", result.ResponseId);
        Assert.True(result.InputTokens > 0); Assert.True(result.OutputTokens > 0);
        Assert.InRange(result.Cost, 0.000001m, 0.10m); Assert.Empty(result.Output.ToolCalls);
        Assert.Equal("CONTINUE", result.Output.Decision);
        Console.WriteLine($"Live Responses evidence: {result.ResponseId}; input={result.InputTokens}; output={result.OutputTokens}; estimated USD={result.Cost}");
    }

    [LiveAgentFact]
    public async Task RealAgentsDeliverPullRequestOnlyInAuthorizedDisposableRepository()
    {
        string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new InvalidOperationException("Live verification requires " + name);
        var owner = Required("LIVE_GITHUB_OWNER"); var repository = Required("LIVE_GITHUB_REPOSITORY");
        if (!repository.StartsWith("aep-sandbox-", StringComparison.Ordinal) || repository == "ai-engineering-platform") throw new InvalidOperationException("Live verification may only mutate an explicit disposable aep-sandbox-* repository.");
        var connection = new RepositoryConnection(owner, repository, long.Parse(Required("LIVE_GITHUB_INSTALLATION_ID")), Environment.GetEnvironmentVariable("LIVE_GITHUB_DEFAULT_BRANCH") ?? "main");
        var apiKey = Required("OPENAI_API_KEY"); var model = Required("OPENAI_DEFAULT_MODEL");
        var inputPrice = decimal.Parse(Required("LIVE_MODEL_INPUT_PRICE"), System.Globalization.CultureInfo.InvariantCulture);
        var outputPrice = decimal.Parse(Required("LIVE_MODEL_OUTPUT_PRICE"), System.Globalization.CultureInfo.InvariantCulture);
        var budget = decimal.Parse(Required("LIVE_MAX_BUDGET_USD"), System.Globalization.CultureInfo.InvariantCulture);
        if (budget is <= 0 or > 5 || inputPrice <= 0 || outputPrice <= 0) throw new InvalidOperationException("Set positive prices and an explicit live budget no greater than $5.");
        var token = (await File.ReadAllTextAsync(Required("LIVE_EXECUTOR_TOKEN_FILE"))).Trim();
        using var githubHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var source = new GitHubSourceControl(githubHttp, Required("GITHUB_APP_ID"), Required("GITHUB_APP_PRIVATE_KEY_PATH"), null);
        await source.ExecuteAsync(connection, "ReadRepository", JsonSerializer.SerializeToElement(new { }), Guid.NewGuid().ToString("N"), default);
        await using var factory = new PlatformFactory();
        await using var live = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(new OpenAIProvider(apiKey));
            services.RemoveAll<IExecutionSandbox>(); services.AddSingleton<IExecutionSandbox>(new UnixExecutionSandbox(Required("LIVE_EXECUTOR_SOCKET_PATH"), token));
            services.RemoveAll<ISourceControl>(); services.AddSingleton<ISourceControl>(source);
        }));
        using var client = live.CreateClient(); client.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { password = PlatformFactory.Password }); login.EnsureSuccessStatusCode();
        var store = live.Services.GetRequiredService<IPlatformStore>();
        for (var attempt = 0; (await store.AgentsAsync(default)).Count < 7 && attempt < 50; attempt++) await Task.Delay(100);
        foreach (var agent in await store.AgentsAsync(default)) await store.SaveAgentAsync(agent with { Model = model, MaxOutputTokens = 2500, InputCostPerMillion = inputPrice, OutputCostPerMillion = outputPrice }, default);
        var project = new Project { Name = "Live disposable engineering verification", OriginalContext = "This is an explicitly disposable local-runtime verification repository. Inspect its actual source and tests. Implement one small useful change to its existing feature (for the standard fixture, change answer() to return 2 and update its tests). Use its existing stack without installing dependencies or adding dummy frontend code. Preserve compatibility except this explicit behavior change. Write/execute real tests, map each selected requirement ID to RunTests evidence, and review the actual diff. Do not modify any other repository or default branch.", RepositoryConnection = connection, ExecutionPolicy = new(MaxIterations: 3, MaxToolCalls: 60, MaxDurationMinutes: 15, MaxBudgetUsd: budget) };
        await store.SaveProjectAsync(project, null, default);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/runs", new { }); response.EnsureSuccessStatusCode();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var run = (await response.Content.ReadFromJsonAsync<ExecutionRun>(json))!;
        var approved = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        while (!deadline.IsCancellationRequested)
        {
            run = (await client.GetFromJsonAsync<ExecutionRun>("/api/v1/runs/" + run.Id, json, deadline.Token))!;
            if (run.CurrentStage == RunStage.Failed) throw new Exception("Live stage failed: " + run.FailureReason);
            if (run.CurrentStage == RunStage.Completed) break;
            if (run.CurrentStage == RunStage.AwaitingHumanApproval)
            {
                // Opting into this test explicitly authorizes delivery only in the disposable allowlisted repository.
                var approvals = await store.RecordsAsync("approval", project.Id, run.Id, null, 0, 0, 100, deadline.Token);
                var approval = approvals.Last();
                if (approval.Status == "Pending") { var decision = await client.PostAsJsonAsync($"/api/v1/approvals/{approval.Id}/decision", new { approve = true, reason = "Owner-authorized opt-in disposable live verification" }, deadline.Token); decision.EnsureSuccessStatusCode(); approved = true; }
            }
            await Task.Delay(1000, deadline.Token);
        }
        Assert.Equal(RunStage.Completed, run.CurrentStage); Assert.True(approved); Assert.True(run.TokenUsage > 0); Assert.InRange(run.Cost, 0.000001m, budget);
        var artifacts = await store.RecordsAsync("artifact", project.Id, run.Id, null, 0, 0, 100, default);
        var pr = Assert.Single(artifacts, item => item.Name == "PullRequest");
        using var document = JsonDocument.Parse(pr.Content); Assert.True(document.RootElement.GetProperty("number").GetInt32() > 0);
        Assert.Contains(artifacts, item => item.Name == "CodeReview" && item.Content.Contains("html_url", StringComparison.Ordinal));
    }
}
