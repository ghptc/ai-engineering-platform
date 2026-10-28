using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;

public sealed class ClarificationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    [Fact]
    public async Task ConversationContinueDoesNotResumeRunButExplicitResponseResumesCheckpoint()
    {
        var model = new ClarifyingModel();
        await using var fixture = new PlatformFactory();
        await using var app = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(model); }));
        using var client = await Login(app);
        var store = app.Services.GetRequiredService<IPlatformStore>();
        var project = new Project { Name = "Clarification fixture", OriginalContext = "Ask the owner before planning.", RepositoryConnection = new("fixture", "repo", 1) };
        await store.SaveProjectAsync(project, null, default);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "clarification-test");
        var started = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/runs", new { }); started.EnsureSuccessStatusCode();
        var run = (await started.Content.ReadFromJsonAsync<ExecutionRun>(Json))!;
        run = await Wait(client, run.Id, RunStage.AwaitingHumanInput);
        Assert.False(run.IsRunnable); Assert.Equal(RunStage.Analyzing, run.ResumeStage);
        Assert.Equal(0, run.RetryCount); Assert.True(run.Cost > 0);
        var request = Assert.Single(run.HumanInputs); Assert.Contains("authorize", request.Questions[0]);
        // Conversation-only CONTINUE with five requirements is advice, not an execution checkpoint.
        var conversationResponse = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/conversations", new { });
        var conversation = (await conversationResponse.Content.ReadFromJsonAsync<RecordEntry>(Json))!;
        (await client.PostAsJsonAsync($"/api/v1/conversations/{conversation.Id}/messages", new { content = "I authorize the requested scope." })).EnsureSuccessStatusCode();
        var replyResponse = await client.PostAsJsonAsync($"/api/v1/conversations/{conversation.Id}/reply", new { }); replyResponse.EnsureSuccessStatusCode();
        var reply = (await replyResponse.Content.ReadFromJsonAsync<RecordEntry>(Json))!;
        Assert.Null(reply.RunId); Assert.Equal("ConversationOnly", reply.Metadata["scope"]);
        Assert.Equal("CONTINUE", JsonSerializer.Deserialize<AgentResult>(reply.Content)!.Decision);
        Assert.Equal(RunStage.AwaitingHumanInput, (await client.GetFromJsonAsync<ExecutionRun>($"/api/v1/runs/{run.Id}", Json))!.CurrentStage);
        Assert.Empty(await store.RecordsAsync("requirement", project.Id, run.Id, null, 0, 0, 100, default));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/clarifications/wrong/response", new { content = "yes" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/clarifications/{request.Id}/response", new { content = " " })).StatusCode);
        var path = $"/api/v1/runs/{run.Id}/clarifications/{request.Id}/response";
        var answers = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(path, new { content = "Owner authorizes this bounded scope." })));
        Assert.Contains(answers, x => x.StatusCode == HttpStatusCode.Accepted);
        Assert.All(answers, x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.Accepted, HttpStatusCode.Conflict }));
        (await client.PostAsJsonAsync(path, new { content = "Owner authorizes this bounded scope." })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, new { content = "different response" })).StatusCode);
        run = await WaitForQuestion(client, run.Id, AgentRole.Architect);
        Assert.Equal(RunStage.Planning, run.ResumeStage); Assert.Equal(1, run.ContextRevision); Assert.Equal(0, run.RetryCount);
        Assert.Equal(5, run.RequirementIds.Length);
        Assert.Equal(5, (await store.RecordsAsync("requirement", project.Id, run.Id, null, 0, 0, 100, default)).Count);
        Assert.Contains(await store.RecordsAsync("artifact", project.Id, run.Id, null, 0, 0, 100, default), x => x.Name == "Requirements" && JsonSerializer.Deserialize<AgentResult>(x.Content)?.Decision == "CONTINUE");
        var analystCalls = model.Contexts.Count(x => x.Definition.Role == AgentRole.BusinessAnalyst && x.Run.Id == run.Id);
        var architectureRequest = run.HumanInputs.Last();
        (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/clarifications/{architectureRequest.Id}/response", new { content = "Owner confirms architecture." })).EnsureSuccessStatusCode();
        run = await Wait(client, run.Id, RunStage.AwaitingHumanApproval);
        Assert.Equal(analystCalls, model.Contexts.Count(x => x.Definition.Role == AgentRole.BusinessAnalyst && x.Run.Id == run.Id));
        Assert.Equal(2, run.ContextRevision); Assert.Equal(1, run.Iteration);
        Assert.All(run.HumanInputs, x => { Assert.NotNull(x.RespondedAt); Assert.Equal("Owner", x.RespondedBy); });
        Assert.Contains(model.Contexts, x => x.Run.Id == run.Id && x.Definition.Role == AgentRole.BusinessAnalyst && x.Context.Contains("Owner authorizes this bounded scope."));
        var reread = await new MongoPlatformStore(Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028", fixture.Database).GetRunAsync(run.Id, default);
        Assert.Equal(JsonSerializer.Serialize(run.HumanInputs), JsonSerializer.Serialize(reread!.HumanInputs));
        Assert.Single(await store.RunsAsync(project.Id, 0, 100, default));
        await WaitForEvents(store, run, "HumanInputRequested", "HumanInputReceived");
    }

    [Fact]
    public async Task RetryReloadsClientMessagesAndDoesNotReuseFailedAgentCheckpoint()
    {
        var model = new ClarifyingModel { FailFirst = true };
        await using var fixture = new PlatformFactory();
        await using var app = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(model); }));
        using var client = await Login(app);
        var store = app.Services.GetRequiredService<IPlatformStore>();
        var project = new Project { Name = "Retry fixture", OriginalContext = "Original snapshot" };
        await store.SaveProjectAsync(project, null, default);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "retry-context");
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/runs", new { }); response.EnsureSuccessStatusCode();
        var run = (await response.Content.ReadFromJsonAsync<ExecutionRun>(Json))!;
        run = await Wait(client, run.Id, RunStage.Failed);
        Assert.Contains("invalid requirements", run.FailureReason);
        Assert.Empty(await store.RecordsAsync("requirement", project.Id, run.Id, null, 0, 0, 100, default));
        var c = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/conversations", new { });
        var conversation = (await c.Content.ReadFromJsonAsync<RecordEntry>(Json))!;
        (await client.PostAsJsonAsync($"/api/v1/conversations/{conversation.Id}/messages", new { content = "Fresh retry authorization" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/retry", new { })).EnsureSuccessStatusCode();
        run = await WaitForQuestion(client, run.Id, AgentRole.Architect);
        Assert.Equal(1, run.RetryCount); Assert.Equal(5, run.RequirementIds.Length);
        Assert.Contains(model.Contexts, x => x.Run.RetryCount == 1 && x.Context.Contains("Fresh retry authorization") && x.Context.Contains("Original snapshot"));
        Assert.Equal(2, model.Contexts.Count(x => x.Definition.Role == AgentRole.BusinessAnalyst));
    }

    [Fact]
    public async Task LegacyBlockedFailureCanRecoverWithoutResettingExhaustedRetries()
    {
        var model = new ClarifyingModel();
        await using var fixture = new PlatformFactory();
        await using var app = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(model); }));
        using var client = await Login(app);
        var store = app.Services.GetRequiredService<IPlatformStore>();
        var project = new Project { Name = "Legacy clarification recovery" }; await store.SaveProjectAsync(project, null, default);
        var run = new ExecutionRun { ProjectId = project.Id, ProjectSnapshot = project, IdempotencyKey = "legacy", CurrentStage = RunStage.Failed, ResumeStage = RunStage.Analyzing, RetryCount = 2, FailureReason = "Human clarification required: Authorize scope", Cost = 0.02m, TokenUsage = 200 };
        await store.CreateRunAsync(run, default);
        var blocked = new ModelExecutionResult(new AgentResult("Authorize scope", "BLOCKED", [], [], [], [], "", OpenQuestions: ["Authorize scope?"]), "test-only-legacy", 100, 100, 0.02m);
        await store.AppendAsync(new() { Kind = "task", ProjectId = project.Id, RunId = run.Id, Name = "BusinessAnalyst", Content = JsonSerializer.Serialize(blocked) }, default);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/retry", new { })).StatusCode);
        var recovered = await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/clarifications/recover", new { }); recovered.EnsureSuccessStatusCode();
        var waiting = (await recovered.Content.ReadFromJsonAsync<ExecutionRun>(Json))!;
        Assert.Equal(RunStage.AwaitingHumanInput, waiting.CurrentStage); Assert.Equal(2, waiting.RetryCount); Assert.Equal(0.02m, waiting.Cost); Assert.Equal(run.StartedAt, waiting.StartedAt);
        var question = Assert.Single(waiting.HumanInputs);
        (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/clarifications/recover", new { })).EnsureSuccessStatusCode();
        using var anonymous = app.CreateClient(); anonymous.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/v1/runs/{run.Id}/clarifications/{question.Id}/response", new { content = "yes" })).StatusCode);
        (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/clarifications/{question.Id}/response", new { content = "Owner explicitly authorizes scope." })).EnsureSuccessStatusCode();
        var resumed = await WaitForQuestion(client, run.Id, AgentRole.Architect);
        Assert.Equal(2, resumed.RetryCount); Assert.Equal(5, resumed.RequirementIds.Length); Assert.True(resumed.Cost >= 0.02m);
        Assert.True(await store.SaveRunAsync(resumed with { Cost = resumed.Policy.MaxBudgetUsd }, resumed.Version, null, default));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/clarifications/{resumed.HumanInputs.Last().Id}/response", new { content = "Proceed beyond budget" })).StatusCode);
        Assert.Equal(RunStage.AwaitingHumanInput, (await store.GetRunAsync(run.Id, default))!.CurrentStage);
        // A normal failure, even at the same stage, cannot bypass retries via this endpoint.
        var other = new Project { Name = "Normal failure" }; await store.SaveProjectAsync(other, null, default);
        var failed = run with { Id = Guid.NewGuid().ToString("N"), ProjectId = other.Id, ProjectSnapshot = other, FailureReason = "Execution budget exhausted." };
        await store.CreateRunAsync(failed, default);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/runs/{failed.Id}/clarifications/recover", new { })).StatusCode);
    }

    [Fact]
    public async Task PendingQuestionSurvivesApplicationRestartAndResumesSameRun()
    {
        var model = new ClarifyingModel();
        await using var fixture = new PlatformFactory();
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateApp() => fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(model); }));
        string runId; string questionId;
        await using (var first = CreateApp())
        {
            using var client = await Login(first);
            var store = first.Services.GetRequiredService<IPlatformStore>();
            var project = new Project { Name = "Restart clarification" }; await store.SaveProjectAsync(project, null, default);
            client.DefaultRequestHeaders.Add("Idempotency-Key", "restart-clarification");
            var response = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/runs", new { }); response.EnsureSuccessStatusCode();
            var run = (await response.Content.ReadFromJsonAsync<ExecutionRun>(Json))!;
            var waiting = await WaitForQuestion(client, run.Id, AgentRole.BusinessAnalyst);
            runId = run.Id; questionId = waiting.HumanInputs.Last().Id;
        }
        await using var restarted = CreateApp();
        using var resumedClient = await Login(restarted);
        var persisted = (await resumedClient.GetFromJsonAsync<ExecutionRun>($"/api/v1/runs/{runId}", Json))!;
        Assert.Equal(RunStage.AwaitingHumanInput, persisted.CurrentStage); Assert.Equal(questionId, persisted.HumanInputs.Last().Id);
        (await resumedClient.PostAsJsonAsync($"/api/v1/runs/{runId}/clarifications/{questionId}/response", new { content = "Owner authorizes after restart." })).EnsureSuccessStatusCode();
        var planning = await WaitForQuestion(resumedClient, runId, AgentRole.Architect);
        Assert.Equal(5, planning.RequirementIds.Length); Assert.Equal(1, planning.ContextRevision);
        Assert.Equal(2, model.Contexts.Count(x => x.Definition.Role == AgentRole.BusinessAnalyst));
    }

    [LiveAgentFact]
    public async Task RealOpenAIClarificationResumesAnalysisWithoutRepositoryOperations()
    {
        string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new InvalidOperationException("Live verification requires " + name);
        var inputPrice = decimal.Parse(Required("LIVE_MODEL_INPUT_PRICE"), System.Globalization.CultureInfo.InvariantCulture);
        var outputPrice = decimal.Parse(Required("LIVE_MODEL_OUTPUT_PRICE"), System.Globalization.CultureInfo.InvariantCulture);
        var provider = new OpenAIProvider(Required("OPENAI_API_KEY"));
        await using var fixture = new PlatformFactory();
        await using var app = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(new LiveAnalyst(provider)); }));
        using var client = await Login(app);
        var store = app.Services.GetRequiredService<IPlatformStore>();
        for (var attempt = 0; (await store.AgentsAsync(default)).Count < 7 && attempt < 50; attempt++) await Task.Delay(100);
        var analyst = (await store.AgentsAsync(default)).Single(x => x.Role == AgentRole.BusinessAnalyst);
        await store.SaveAgentAsync(analyst with { Model = Required("OPENAI_DEFAULT_MODEL"), InputCostPerMillion = inputPrice, OutputCostPerMillion = outputPrice, MaxOutputTokens = 2000, AllowedTools = [], SystemInstructions = "This is a real clarification integration test. If ExecutionClarifications has no answered owner input, return BLOCKED asking whether the owner authorizes a dependency-free local notes feature. After the owner explicitly authorizes it, return CONTINUE with one small structured requirement, concrete acceptance criteria, no open questions and no tools. Do not claim implementation or test execution." }, default);
        var project = new Project { Name = "Real BA clarification verification", OriginalContext = "Analyze a dependency-free local notes feature. Ask for explicit owner authorization before requirements planning.", ExecutionPolicy = new(MaxBudgetUsd: 0.25m, MaxDurationMinutes: 5) };
        await store.SaveProjectAsync(project, null, default);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var start = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/runs", new { }); start.EnsureSuccessStatusCode();
        var run = (await start.Content.ReadFromJsonAsync<ExecutionRun>(Json))!;
        run = await WaitForQuestion(client, run.Id, AgentRole.BusinessAnalyst);
        var response = await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/clarifications/{run.HumanInputs.Last().Id}/response", new { content = "I explicitly authorize the dependency-free local notes feature. Proceed with one bounded requirement and its acceptance criteria." }); response.EnsureSuccessStatusCode();
        run = await WaitForQuestion(client, run.Id, AgentRole.Architect);
        Assert.NotEmpty(run.RequirementIds); Assert.Equal(1, run.ContextRevision);
        Assert.InRange(run.Cost, 0.000001m, 0.25m); Assert.Equal(0, run.ToolCalls);
        var tasks = (await store.RecordsAsync("task", project.Id, run.Id, null, 0, 0, 100, default)).Where(x => x.Name == "BusinessAnalyst").Select(x => JsonSerializer.Deserialize<ModelExecutionResult>(x.Content)!).ToArray();
        Assert.Equal(2, tasks.Length); Assert.All(tasks, task => Assert.StartsWith("resp_", task.ResponseId));
        Assert.Equal(new[] { "BLOCKED", "CONTINUE" }, tasks.Select(x => x.Output.Decision));
        Console.WriteLine($"Real BA clarification: {string.Join(", ", tasks.Select(x => x.ResponseId))}; input={tasks.Sum(x => x.InputTokens)}; output={tasks.Sum(x => x.OutputTokens)}; estimated USD={tasks.Sum(x => x.Cost)}");
        (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/cancel", new { })).EnsureSuccessStatusCode();
    }
    private sealed class LiveAnalyst(ILanguageModelProvider provider) : ILanguageModelProvider
    {
        public Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct) => context.Definition.Role == AgentRole.BusinessAnalyst
            ? provider.ExecuteAsync(context, ct)
            : Task.FromResult(new ModelExecutionResult(new AgentResult("Stop after real BA verification; no repository work authorized by this test.", "BLOCKED", [], [], [], [], "", OpenQuestions: ["End of the BA-only live verification."]), "test-only-stop", 0, 0, 0));
    }

    private static async Task<HttpClient> Login(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient(); client.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { password = PlatformFactory.Password })).EnsureSuccessStatusCode(); return client;
    }
    private static async Task<ExecutionRun> Wait(HttpClient client, string id, RunStage stage)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        while (true)
        {
            var run = (await client.GetFromJsonAsync<ExecutionRun>($"/api/v1/runs/{id}", Json, deadline.Token))!;
            if (run.CurrentStage == stage) return run;
            if (run.CurrentStage == RunStage.Failed) throw new Exception(run.FailureReason);
            await Task.Delay(50, deadline.Token);
        }
    }
    private static async Task<ExecutionRun> WaitForQuestion(HttpClient client, string id, AgentRole role)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        while (true)
        {
            var run = (await client.GetFromJsonAsync<ExecutionRun>($"/api/v1/runs/{id}", Json, deadline.Token))!;
            if (run.CurrentStage == RunStage.AwaitingHumanInput && run.HumanInputs.Last().AgentRole == role) return run;
            if (run.CurrentStage == RunStage.Failed) throw new Exception(run.FailureReason);
            await Task.Delay(50, deadline.Token);
        }
    }
    private static async Task WaitForEvents(IPlatformStore store, ExecutionRun run, params string[] names)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var events = await store.RecordsAsync("event", run.ProjectId, run.Id, null, 0, 0, 500, deadline.Token);
            if (names.All(name => events.Any(x => x.Name == name))) return;
            await Task.Delay(50, deadline.Token);
        }
    }
    private sealed class ClarifyingModel : ILanguageModelProvider
    {
        public ConcurrentBag<AgentContext> Contexts { get; } = [];
        public bool FailFirst { get; init; }
        private readonly DeterministicModel fallback = new();
        public async Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
        {
            Contexts.Add(context);
            var result = await fallback.ExecuteAsync(context, ct);
            if (context.Definition.Role == AgentRole.BusinessAnalyst)
            {
                if (FailFirst && context.Run.RetryCount == 0) return result with { Output = result.Output with { Requirements = [new("Invalid", "", "", [], 1, [], false)] } };
                if (!FailFirst && context.Context.Contains("ExecutionClarifications") && !context.Run.HumanInputs.Any(x => x.AgentRole == AgentRole.BusinessAnalyst && x.Response != null))
                    return result with { Output = result.Output with { Summary = "Please authorize the bounded scope.", Decision = "BLOCKED", OpenQuestions = ["Do you authorize this bounded scope?"] } };
                return result with { Output = result.Output with { Requirements = Enumerable.Range(1, 5).Select(i => new RequirementProposal("Requirement " + i, "Traceable fixture behavior", "As owner I need evidence", ["Verify scenario " + i], 1, [], false)).ToArray() } };
            }
            if (context.Definition.Role == AgentRole.Architect && !context.Run.HumanInputs.Any(x => x.AgentRole == AgentRole.Architect && x.Response != null))
                return result with { Output = result.Output with { Decision = "BLOCKED", Summary = "Confirm architecture.", OpenQuestions = ["Confirm architecture?"] } };
            return result;
        }
    }
}
