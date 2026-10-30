using AIEngineeringPlatform.Agents;
using AIEngineeringPlatform.Application;
using AIEngineeringPlatform.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AIEngineeringPlatform.Integration.Tests;

public sealed partial class RuntimeFlowTests
{
    [Fact]
    public async Task ExistingFrontendInstructionsAreVersionedWithoutExpandingTools()
    {
        await using var root = new PlatformFactory();
        var store = new AIEngineeringPlatform.Infrastructure.MongoPlatformStore(Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028", root.Database);
        await store.InitializeAsync(default);
        var original = AgentCatalog.Defaults("deterministic-test-model").Single(x => x.Role == AgentRole.FrontendEngineer);
        original = original with { SystemInstructions = original.SystemInstructions.Replace(AgentCatalog.ImplementationInstructions, ""), InstructionVersion = 12 };
        await store.SaveAgentAsync(original, default);
        await using var factory = root.WithWebHostBuilder(_ => { });
        using var client = factory.CreateClient();
        await Login(client);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        AgentDefinition migrated;
        while (true)
        {
            migrated = (await store.AgentsAsync(timeout.Token)).Single(x => x.Role == AgentRole.FrontendEngineer);
            if (migrated.SystemInstructions.Contains(AgentCatalog.ImplementationInstructions)) break;
            await Task.Delay(50, timeout.Token);
        }
        Assert.True(migrated.InstructionVersion > original.InstructionVersion);
        Assert.Equal(original.Model, migrated.Model);
        Assert.Equal(original.AllowedTools, migrated.AllowedTools);
        var versions = await store.RecordsAsync("instructionVersion", null, null, null, 0, 0, 1000, default);
        var archived = Assert.Single(versions, x => x.Name == original.Id && x.Metadata.GetValueOrDefault("migration") == "greenfield-implementation-handoff");
        Assert.DoesNotContain(AgentCatalog.ImplementationInstructions, JsonSerializer.Deserialize<AgentDefinition>(archived.Content)!.SystemInstructions);
        Assert.Equal(1, migrated.SystemInstructions.Split(AgentCatalog.ImplementationInstructions).Length - 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GreenfieldFrontendReceivesPlanningAndBackendHandoffBeforeWritingWithoutProfilesOrPr(bool focused)
    {
        await using var root = new PlatformFactory();
        var sandbox = new GreenfieldSandbox();
        var model = new GreenfieldModel();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILanguageModelProvider>(); services.AddSingleton<ILanguageModelProvider>(model);
            services.RemoveAll<IExecutionSandbox>(); services.AddSingleton<IExecutionSandbox>(sandbox);
            services.RemoveAll<ISourceControl>(); services.AddSingleton<ISourceControl>(new NoPrSource());
        }));
        using var client = factory.CreateClient(); await Login(client);
        var store = factory.Services.GetRequiredService<IPlatformStore>();
        ExecutionRun run;
        if (focused) run = await Start(client, store, ExecutionPresets.StandardDevelopment, planning: true);
        else
        {
            var project = new Project { Name = "Greenfield", OriginalContext = "Implement React TypeScript Ant Design registration and commits", RepositoryConnection = new("owner", "fixture", 1) };
            await store.SaveProjectAsync(project, null, default);
            client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            var response = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/runs", new { });
            response.EnsureSuccessStatusCode(); run = (await response.Content.ReadFromJsonAsync<ExecutionRun>(Json))!;
        }
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.Paused, run.CurrentStage);
        Assert.Equal(RunStage.RunningTests, run.ResumeStage);
        Assert.Equal(ExecutionOutcome.Blocked, run.Outcome);
        Assert.True(model.FrontendInspected);
        Assert.Equal("backend preserved", sandbox.Files["backend/Feature.cs"]);
        Assert.Contains("antd", sandbox.Files["frontend/package.json"]);
        Assert.Contains("React", sandbox.Files["frontend/src/main.tsx"]);
        Assert.Contains("frontend/src/App.tsx", sandbox.Files.Keys);
        Assert.Contains("frontend/tsconfig.json", sandbox.Files.Keys);
        Assert.Contains("frontend/vite.config.ts", sandbox.Files.Keys);
        Assert.Contains("frontend/index.html", sandbox.Files.Keys);
        Assert.Contains("backend/FeatureTests.cs", sandbox.Files.Keys);
        Assert.DoesNotContain(sandbox.Operations, x => x is "RunTests" or "RunCommand" or "SelectBranch" or "CreateCommit" or "PushBranch");
        var checkpoints = await store.RecordsAsync("task", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.Contains(checkpoints, x => x.Name == "BackendEngineer");
        Assert.Contains(checkpoints, x => x.Name == "TestEngineer");
        Assert.DoesNotContain(checkpoints, x => x.Name is "QAEngineer" or "CodeReviewer");
        var before = sandbox.Files.ToArray();
        (await client.PostAsJsonAsync($"/api/v1/runs/{run.Id}/resume", new { })).EnsureSuccessStatusCode();
        run = await Wait(store, run.Id, r => !r.IsRunnable);
        Assert.Equal(RunStage.RunningTests, run.ResumeStage);
        Assert.Equal(before, sandbox.Files.ToArray());
        var after = await store.RecordsAsync("task", run.ProjectId, run.Id, null, 0, 0, 1000, default);
        Assert.All(checkpoints, x => Assert.Single(after, y => y.Id == x.Id && y.Content == x.Content));
    }

    private sealed class GreenfieldModel : ILanguageModelProvider
    {
        private readonly DeterministicModel inner = new();
        public bool FrontendInspected;
        private int frontendCalls;
        public async Task<ModelExecutionResult> ExecuteAsync(AgentContext context, CancellationToken ct)
        {
            var result = await inner.ExecuteAsync(context, ct);
            if (context.Definition.Role == AgentRole.Architect)
                return result with { Output = result.Output with { Planning = new([], ["Offline cache unverified"], [new("Delivery", "Approved tests unavailable", "fixture-profile-evidence")], "WriteFile frontend/package.json and React source", "Verify offline React TypeScript Ant Design dependencies", "Create executable component tests; await approved profiles") } };
            if (context.Definition.Role == AgentRole.BackendEngineer)
                return result with { Output = result.Output with { Summary = "Backend partial; endpoints still missing", NextContext = "Preserve backend files; implement frontend against specified API contract", ToolCalls = context.Context.Contains("written backend/Feature.cs") ? [] : [new("WriteFile", JsonSerializer.SerializeToElement(new { path = "backend/Feature.cs", content = "backend preserved" }))] } };
            if (context.Definition.Role != AgentRole.FrontendEngineer) return result;
            Assert.Contains(AgentCatalog.ImplementationInstructions, context.Definition.SystemInstructions);
            Assert.Contains("WriteFile", context.Definition.AllowedTools);
            Assert.Contains("ApplyPatch", context.Definition.AllowedTools);
            Assert.Contains("ReconcileRepository", context.Definition.AllowedTools);
            // These must be present in the actual provider context on both execution routes.
            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(context.Context));
            using var envelope = JsonDocument.ParseValue(ref reader);
            var json = envelope.RootElement;
            var planning = context.Run.CurrentTask == null ? json.GetProperty("CurrentAnalysis").EnumerateArray().Select(x => x.GetProperty("Planning")).Single(x => x.GetProperty("BootstrapStrategy").GetString()!.Contains("frontend/package.json")) : json.GetProperty("ArchitecturalConstraints")[0].GetProperty("Planning");
            Assert.Contains("frontend/package.json", planning.GetProperty("BootstrapStrategy").GetString());
            Assert.Contains("offline", planning.GetProperty("DependencyStrategy").GetString());
            Assert.Contains("approved", planning.GetProperty("TestStrategy").GetString());
            Assert.Contains("Backend partial", json.GetProperty("PreviousHandoff").GetProperty("SummaryPreview").GetString());
            Assert.Contains("Preserve backend", json.GetProperty("PreviousHandoff").GetProperty("NextActionPreview").GetString());
            if (++frontendCalls == 1)
                return result with { Output = result.Output with { ToolCalls = [new("ReadRepository", JsonSerializer.SerializeToElement(new { })), new("ReconcileRepository", JsonSerializer.SerializeToElement(new { })), new("ListFiles", JsonSerializer.SerializeToElement(new { }))] } };
            FrontendInspected = true;
            if (context.Context.Contains("written frontend/package.json")) return result with { Output = result.Output with { ToolCalls = [] } };
            var files = new Dictionary<string, string>
            {
                ["frontend/package.json"] = "{\"dependencies\":{\"react\":\"19.0.0\",\"react-dom\":\"19.0.0\",\"antd\":\"5.24.0\"},\"devDependencies\":{\"typescript\":\"5.7.3\",\"vite\":\"6.1.0\"}}",
                ["frontend/index.html"] = "<div id='root'></div><script type='module' src='/src/main.tsx'></script>",
                ["frontend/tsconfig.json"] = "{\"compilerOptions\":{\"jsx\":\"react-jsx\",\"strict\":true}}",
                ["frontend/vite.config.ts"] = "import {defineConfig} from 'vite'; export default defineConfig({});",
                ["frontend/src/main.tsx"] = "import React from 'react'; import {createRoot} from 'react-dom/client'; import App from './App'; createRoot(document.getElementById('root')!).render(<App/>);",
                ["frontend/src/App.tsx"] = "import {Empty} from 'antd'; export default function App(){return <Empty description='Register a repository'/>;}"
            };
            return result with { Output = result.Output with { ToolCalls = files.Select(x => new ToolCall("WriteFile", JsonSerializer.SerializeToElement(new { path = x.Key, content = x.Value }))).ToArray() } };
        }
    }

    private sealed class GreenfieldSandbox : IExecutionSandbox
    {
        private readonly DeterministicSandbox inner = new();
        public Dictionary<string, string> Files { get; } = new() { ["README.md"] = "Greenfield fixture" };
        public List<string> Operations => inner.Operations;
        public bool Configured => true;
        public async Task<SandboxResponse> ExecuteAsync(SandboxRequest request, CancellationToken ct)
        {
            var result = await inner.ExecuteAsync(request, ct);
            if (request.Operation == "WriteFile") Files[request.Arguments.GetProperty("path").GetString()!] = request.Arguments.GetProperty("content").GetString()!;
            if (request.Operation == "ReadRepository") return result with { Output = JsonSerializer.Serialize(new { Files = Files.Keys, HasSourceChanges = Files.Count > 1, TestProfiles = Array.Empty<string>(), Capabilities = new { BootstrapOperations = new[] { "WriteFile", "ApplyPatch" }, Profiles = Array.Empty<object>() } }) };
            if (request.Operation == "ListFiles") return result with { Output = string.Join('\n', Files.Keys) };
            return result;
        }
    }
    private sealed class NoPrSource : ISourceControl
    {
        public bool Configured => true;
        public bool VerifyWebhook(ReadOnlySpan<byte> payload, string signature) => false;
        public Task<string> ExecuteAsync(RepositoryConnection repo, string operation, JsonElement args, string operationId, CancellationToken ct)
        {
            Assert.Equal("ReconcileRepository", operation);
            return Task.FromResult("{\"branches\":[],\"pullRequests\":[]}");
        }
    }
}
