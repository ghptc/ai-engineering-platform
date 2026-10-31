using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AIEngineeringPlatform.Application.Estimation;
using AIEngineeringPlatform.Domain;
using AIEngineeringPlatform.Infrastructure;
using Xunit;
namespace AIEngineeringPlatform.Integration.Tests;
public sealed class RequirementIntakeTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static async Task<(HttpClient Client, string Project)> OwnerAsync(PlatformFactory factory)
    {
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Requested-With", "AIEngineeringPlatform");
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { password = PlatformFactory.Password })).EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync("/api/v1/projects", new { name = "Requirements fixture", description = "Disposable M6 test", originalContext = "", technologyStack = new[] { ".NET" } });
        response.EnsureSuccessStatusCode(); return (client, (await response.Content.ReadFromJsonAsync<Project>(Json))!.Id);
    }
    private static async Task<HttpResponseMessage> ImportAsync(HttpClient client, string project, string text, string? revision = null, string name = "requirements.md")
    {
        var bytes = new ByteArrayContent(Encoding.UTF8.GetBytes(text)); bytes.Headers.ContentType = new("application/octet-stream");
        return await client.PostAsync($"/api/v1/projects/{project}/requirement-scopes/import?documentKey=product&fileName={name}" + (revision == null ? "" : "&expectedRevision=" + revision), bytes);
    }
    private static RequirementReview Review(ScopedRequirement requirement, RequirementImplementationState state = RequirementImplementationState.NotStarted, string[]? dependencies = null) => new(requirement.Description, ["Owner supplied executable acceptance scenario"], 2, RequirementClassification.Functional, dependencies ?? [], RequirementComplexity.Small, state, ["API"], "M1", [AgentRole.BackendEngineer], "Approved backend HTTP integration tests", "Owner review reason");
    private static Task<HttpResponseMessage> ReviewAsync(HttpClient client, RequirementScope scope, ScopedRequirement item, RequirementReview review) => client.PostAsJsonAsync($"/api/v1/projects/{scope.ProjectId}/requirement-scopes/{scope.Revision}/requirements/{item.Id}/review", review, Json);
    [Fact]
    public async Task Imports125RequirementsAndPreservesSourceAcrossReviewsReimportsAndStoreRestart()
    {
        await using var factory = new PlatformFactory(); var (client, project) = await OwnerAsync(factory); using var owner = client;
        var source = "# Product requirements\n" + string.Join('\n', Enumerable.Range(1, 125).Select(i => $"REQ-{i:000}: Actual source requirement {i}"));
        var response = await ImportAsync(owner, project, source); response.EnsureSuccessStatusCode();
        var scope = (await response.Content.ReadFromJsonAsync<RequirementScope>(Json))!;
        Assert.Equal(125, scope.Requirements.Length); Assert.Equal(125, scope.Requirements.Select(x => x.Id).Distinct().Count());
        Assert.Equal("line 2", scope.Requirements[0].Sources[0].Location); Assert.Equal("REQ-001", scope.Requirements[0].Sources[0].OriginalLabel);
        Assert.All(scope.Requirements, r => { Assert.False(r.OwnerReviewed); Assert.Equal(RequirementImplementationState.NotStarted, r.ImplementationState); Assert.Empty(r.AcceptanceCriteria); });
        Assert.False(scope.ReadyForEstimation);
        var reviewedResponse = await ReviewAsync(owner, scope, scope.Requirements[0], Review(scope.Requirements[0])); reviewedResponse.EnsureSuccessStatusCode();
        var reviewed = (await reviewedResponse.Content.ReadFromJsonAsync<RequirementScope>(Json))!;
        Assert.Equal(scope.Revision, reviewed.ParentRevision); Assert.True(reviewed.Requirements[0].OwnerReviewed);
        Assert.Equal(scope.Requirements[0].Sources, reviewed.Requirements[0].Sources);
        var revisedResponse = await ImportAsync(owner, project, source.Replace("Actual source requirement 1\n", "Updated source requirement 1\n"), reviewed.Revision); revisedResponse.EnsureSuccessStatusCode();
        var revised = (await revisedResponse.Content.ReadFromJsonAsync<RequirementScope>(Json))!;
        Assert.Equal(scope.Requirements[0].Id, revised.Requirements[0].Id); Assert.False(revised.Requirements[0].OwnerReviewed);
        Assert.Equal(scope.Document.ExtractedText, source);
        var store = new MongoPlatformStore(Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028", factory.Database);
        Assert.Equal(revised.Revision, (await store.CurrentScopeAsync(project, default))!.Revision);
        Assert.False((await store.ScopeAsync(project, scope.Revision, default))!.Requirements[0].OwnerReviewed);
        Assert.Null(await store.ScopeAsync("different-project", scope.Revision, default));
        Assert.Empty((await owner.GetFromJsonAsync<AIEngineeringPlatform.Application.Page<ExecutionRun>>($"/api/v1/projects/{project}/runs", Json))!.Items);
    }
    [Fact]
    public async Task DuplicateReferencesAndMissingRequirementsAreRetainedWithoutInventingVerification()
    {
        await using var factory = new PlatformFactory(); var (client, project) = await OwnerAsync(factory); using var owner = client;
        var response = await ImportAsync(owner, project, "REQ-001: Same requirement\nREQ-002: Same requirement\nNFR-003: Response within one second\nREQ-004: unclear TBD"); response.EnsureSuccessStatusCode();
        var scope = (await response.Content.ReadFromJsonAsync<RequirementScope>(Json))!;
        Assert.Equal(3, scope.Requirements.Length); Assert.Equal(2, scope.Requirements[0].Sources.Length);
        Assert.Equal(RequirementClassification.NonFunctional, scope.Requirements[1].Classification);
        Assert.Contains(scope.Requirements[2].Warnings, x => x.Contains("TBD", StringComparison.Ordinal));
        var verified = await ReviewAsync(owner, scope, scope.Requirements[0], Review(scope.Requirements[0], RequirementImplementationState.Verified)); Assert.Equal(HttpStatusCode.BadRequest, verified.StatusCode);
        var fewer = await ImportAsync(owner, project, "REQ-001: Same requirement", scope.Revision); fewer.EnsureSuccessStatusCode();
        var next = (await fewer.Content.ReadFromJsonAsync<RequirementScope>(Json))!;
        Assert.Equal(3, next.Requirements.Length); Assert.Equal(2, next.Requirements.Count(x => x.ImplementationState == RequirementImplementationState.Blocked));
        Assert.All(next.Requirements.Where(x => x.ImplementationState == RequirementImplementationState.Blocked), x => Assert.NotEmpty(x.Sources));
    }
    [Fact]
    public async Task ConcurrentReviewsHaveOneWinnerAndCycleOrCrossProjectDependenciesAreRejected()
    {
        await using var factory = new PlatformFactory(); var (client, project) = await OwnerAsync(factory); using var owner = client;
        var imported = await ImportAsync(owner, project, "REQ-1: First\nREQ-2: Second"); imported.EnsureSuccessStatusCode(); var scope = (await imported.Content.ReadFromJsonAsync<RequirementScope>(Json))!;
        var responses = await Task.WhenAll(ReviewAsync(owner, scope, scope.Requirements[0], Review(scope.Requirements[0], dependencies: [scope.Requirements[1].Id])), ReviewAsync(owner, scope, scope.Requirements[1], Review(scope.Requirements[1])));
        Assert.Single(responses, x => x.IsSuccessStatusCode); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var current = (await owner.GetFromJsonAsync<RequirementScope>($"/api/v1/projects/{project}/requirement-scopes/current", Json))!;
        var first = await ReviewAsync(owner, current, current.Requirements[0], Review(current.Requirements[0], dependencies: [current.Requirements[1].Id])); first.EnsureSuccessStatusCode(); var dependencyScope = (await first.Content.ReadFromJsonAsync<RequirementScope>(Json))!;
        var cycle = await ReviewAsync(owner, dependencyScope, dependencyScope.Requirements[1], Review(dependencyScope.Requirements[1], dependencies: [dependencyScope.Requirements[0].Id])); Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);
        var missing = await ReviewAsync(owner, dependencyScope, dependencyScope.Requirements[1], Review(dependencyScope.Requirements[1], dependencies: ["another-project-requirement"])); Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ImportAsync(owner, project, "REQ-3: Stale import", scope.Revision)).StatusCode);
    }
    [Fact]
    public async Task OwnerAuthenticationCsrfAndProjectScopeAreRequired()
    {
        await using var factory = new PlatformFactory(); using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/projects/unknown/requirement-scopes/current")).StatusCode);
        var (client, project) = await OwnerAsync(factory); using var owner = client;
        Assert.Equal("null", await owner.GetStringAsync($"/api/v1/projects/{project}/requirement-scopes/current"));
        Assert.Equal(HttpStatusCode.NotFound, (await ImportAsync(owner, "unknown", "REQ-1: scope")).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-Requested-With");
        Assert.Equal(HttpStatusCode.Forbidden, (await ImportAsync(owner, project, "REQ-1: scope")).StatusCode);
    }
    [Fact]
    public async Task HistoryIsCursorBoundedAndLosingCandidatesNeverBecomeVisible()
    {
        await using var factory = new PlatformFactory(); var (client, project) = await OwnerAsync(factory); using var owner = client;
        var imported = await ImportAsync(owner, project, "REQ-1: History fixture"); imported.EnsureSuccessStatusCode();
        var first = (await imported.Content.ReadFromJsonAsync<RequirementScope>(Json))!; var current = first;
        for (var i = 0; i < 21; i++)
        {
            var response = await ReviewAsync(owner, current, current.Requirements[0], Review(current.Requirements[0]) with { ReviewNote = "Revision " + i });
            response.EnsureSuccessStatusCode(); current = (await response.Content.ReadFromJsonAsync<RequirementScope>(Json))!;
        }
        var history = (await owner.GetFromJsonAsync<ScopeVersionPage>($"/api/v1/projects/{project}/requirement-scopes", Json))!;
        Assert.Equal(20, history.Items.Count); Assert.NotNull(history.NextBefore);
        var older = (await owner.GetFromJsonAsync<ScopeVersionPage>($"/api/v1/projects/{project}/requirement-scopes?before={history.NextBefore}", Json))!;
        Assert.Equal(2, older.Items.Count); Assert.Null(older.NextBefore); Assert.Equal(first.Revision, older.Items[^1].Revision);
        Assert.Empty(history.Items.Select(x => x.Revision).Intersect(older.Items.Select(x => x.Revision)));
        var store = new MongoPlatformStore(Environment.GetEnvironmentVariable("TEST_MONGODB_CONNECTION_STRING") ?? "mongodb://127.0.0.1:27028", factory.Database);
        var stale = current with { Revision = new string('f', 64), ParentRevision = first.Revision, Version = first.Version + 1 };
        Assert.False(await store.AppendScopeAsync(stale, first.Revision, default));
        Assert.Null(await store.ScopeAsync(project, stale.Revision, default));
        Assert.Equal(current.Revision, (await store.CurrentScopeAsync(project, default))!.Revision);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/projects/{project}/requirement-scopes?before=unknown-revision")).StatusCode);
    }
    [Theory]
    [InlineData("requirements.pdf")]
    [InlineData("requirements.docm")]
    [InlineData("requirements.html")]
    public async Task UnsupportedFormatsAreExplicitlyRejected(string name)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => new RequirementDocumentExtractor().ExtractAsync(name, "untrusted"u8.ToArray(), default)); Assert.Contains("not supported", error.Message);
    }
    [Fact]
    public async Task StrictUtf8SizeAndCancellationAreEnforced()
    {
        var extractor = new RequirementDocumentExtractor();
        await Assert.ThrowsAsync<ArgumentException>(() => extractor.ExtractAsync("r.txt", [0xFF, 0xFE], default));
        await Assert.ThrowsAsync<ArgumentException>(() => extractor.ExtractAsync("r.txt", new byte[750001], default));
        await Assert.ThrowsAsync<OperationCanceledException>(() => extractor.ExtractAsync("r.txt", "REQ-1: cancelled"u8.ToArray(), new CancellationToken(true)));
    }
    [Fact]
    public async Task DocxExtractsParagraphReferencesWithoutFollowingRelationshipsAndRejectsMacrosOrDtd()
    {
        var extractor = new RequirementDocumentExtractor();
        var doc = await extractor.ExtractAsync("r.docx", Docx("<w:p><w:r><w:t>REQ-1: Preserve real source</w:t></w:r></w:p>"), default);
        Assert.Equal("paragraph 1", doc.Fragments[0].Location); Assert.Equal("REQ-1: Preserve real source", doc.Fragments[0].Text);
        await Assert.ThrowsAsync<ArgumentException>(() => extractor.ExtractAsync("r.docx", Docx("<w:p><w:r><w:t>text</w:t></w:r></w:p>", macro: true), default));
        await Assert.ThrowsAsync<ArgumentException>(() => extractor.ExtractAsync("r.docx", Docx("", dtd: true), default));
        await Assert.ThrowsAsync<ArgumentException>(() => extractor.ExtractAsync("r.docx", Docx(new string('x', 2_000_001)), default));
    }
    private static byte[] Docx(string body, bool macro = false, bool dtd = false)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open())) writer.Write("<Types><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\" /></Types>");
            using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open())) writer.Write((dtd ? "<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///etc/passwd'>]>" : "") + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" + body + "</w:body></w:document>");
            if (macro) using (var writer = new StreamWriter(zip.CreateEntry("word/vbaProject.bin").Open())) writer.Write("not executed");
        }
        return memory.ToArray();
    }
}
