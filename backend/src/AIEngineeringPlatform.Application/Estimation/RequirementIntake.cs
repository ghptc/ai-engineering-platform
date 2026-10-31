using AIEngineeringPlatform.Domain;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace AIEngineeringPlatform.Application.Estimation;

public sealed record ExtractedRequirementDocument(string MediaType, string Text, DocumentFragment[] Fragments);
public sealed record DocumentFragment(string Location, string Text);
public interface IRequirementDocumentExtractor
{
    Task<ExtractedRequirementDocument> ExtractAsync(string fileName, byte[] bytes, CancellationToken ct);
}
public sealed record ScopeVersion(string Revision, string? ParentRevision, int Version, DateTimeOffset CreatedAt, string Reason, string FileName, int Count, bool ReadyForEstimation);
public sealed record ScopeVersionPage(IReadOnlyList<ScopeVersion> Items, string? NextBefore);
public interface IRequirementScopeStore
{
    Task<RequirementScope?> CurrentScopeAsync(string projectId, CancellationToken ct);
    Task<RequirementScope?> ScopeAsync(string projectId, string revision, CancellationToken ct);
    Task<ScopeVersionPage> ScopeHistoryAsync(string projectId, string? before, CancellationToken ct);
    Task<bool> AppendScopeAsync(RequirementScope scope, string? expectedRevision, CancellationToken ct);
}
public sealed record RequirementReview(string Description, string[] AcceptanceCriteria, int? Priority,
    RequirementClassification Classification, string[] Dependencies, RequirementComplexity Complexity,
    RequirementImplementationState ImplementationState, string[] Components, string? Milestone,
    AgentRole[] Roles, string VerificationStrategy, string ReviewNote);

public sealed partial class RequirementIntake(IPlatformStore projects, IRequirementScopeStore scopes, IRequirementDocumentExtractor extractor)
{
    public const int MaximumUploadBytes = 750_000;
    public const int MaximumTextCharacters = 250_000;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public async Task<RequirementScope?> CurrentAsync(string projectId, CancellationToken ct)
    {
        await ProjectAsync(projectId, ct); return await scopes.CurrentScopeAsync(projectId, ct);
    }
    public async Task<RequirementScope> GetAsync(string projectId, string revision, CancellationToken ct)
    {
        await ProjectAsync(projectId, ct); return await scopes.ScopeAsync(projectId, revision, ct) ?? throw new KeyNotFoundException("Scope revision not found in this project.");
    }
    public async Task<ScopeVersionPage> HistoryAsync(string projectId, string? before, CancellationToken ct)
    {
        await ProjectAsync(projectId, ct); return await scopes.ScopeHistoryAsync(projectId, before, ct);
    }
    private async Task ProjectAsync(string id, CancellationToken ct, bool write = false)
    {
        var p = await projects.GetProjectAsync(id, ct) ?? throw new KeyNotFoundException("Project not found.");
        if (write && p.Status == "Archived") throw new InvalidOperationException("Archived projects cannot change requirements.");
    }
    public async Task<RequirementScope> ImportAsync(string projectId, string documentKey, string fileName, byte[] bytes, string? expectedRevision, CancellationToken ct)
    {
        await ProjectAsync(projectId, ct, true);
        if (bytes.Length is 0 or > MaximumUploadBytes || !DocumentKey().IsMatch(documentKey) || string.IsNullOrWhiteSpace(fileName) || fileName.Length > 180 || fileName.IndexOfAny(['/', '\\', '\0']) >= 0 || fileName.Any(char.IsControl)) throw new ArgumentException("Invalid document name/key or upload size. Maximum upload is 750000 bytes.");
        var previous = await scopes.CurrentScopeAsync(projectId, ct);
        if (previous?.Revision != expectedRevision) throw new InvalidOperationException("Scope changed. Reload before importing.");
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (previous?.Document.Key == documentKey && previous.Document.Sha256 == sha) return previous;
        var doc = await extractor.ExtractAsync(fileName, bytes, ct);
        if (string.IsNullOrWhiteSpace(doc.Text) || doc.Text.Length > MaximumTextCharacters) throw new ArgumentException("Document is empty or extracted text exceeds 250000 characters.");
        var items = new List<ScopedRequirement>(); var warnings = new List<string>();
        var blocks = new List<(string? Label, List<DocumentFragment> Parts)>();
        var explicitItems = doc.Fragments.Any(x => ItemStart().IsMatch(x.Text));
        foreach (var fragment in doc.Fragments)
        {
            ct.ThrowIfCancellationRequested();
            var match = ItemStart().Match(fragment.Text);
            if (match.Success) blocks.Add((match.Groups["label"].Success ? match.Groups["label"].Value : null, [fragment]));
            else if (!explicitItems && !string.IsNullOrWhiteSpace(fragment.Text) && !fragment.Text.TrimStart().StartsWith('#')) blocks.Add((null, [fragment]));
            else if (blocks.Count > 0 && !string.IsNullOrWhiteSpace(fragment.Text) && !fragment.Text.TrimStart().StartsWith('#')) blocks[^1].Parts.Add(fragment);
        }
        if (!explicitItems) warnings.Add("No explicit requirement markers found. Paragraphs are draft candidates; the owner must confirm their meaning.");
        else warnings.Add("Only explicit numbered or bulleted items are draft requirements. Introductory prose and headings remain in the source document for owner review.");
        var normalizedBodies = new Dictionary<string, int>(StringComparer.Ordinal);
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in blocks)
        {
            var body = string.Join('\n', block.Parts.Select(x => x.Text)).Trim();
            if (body.Length > 8000) throw new ArgumentException("A requirement candidate exceeds 8000 characters; split the document into explicit requirements.");
            var source = new RequirementSource(sha, string.Join("; ", block.Parts.Select(x => x.Location)), block.Label, body);
            var normalized = Normalize(ItemStart().Replace(body, "", 1));
            if (normalizedBodies.TryGetValue(normalized, out var duplicate)) { items[duplicate] = items[duplicate] with { Sources = [.. items[duplicate].Sources, source], Warnings = [.. items[duplicate].Warnings, "Duplicate text grouped; all original locations retained."] }; continue; }
            var notes = new List<string> { "Acceptance criteria, priority, dependencies, complexity, components, roles and milestone require review. No repository assessment has been performed." };
            var ambiguous = AmbiguityMarkers().Match(body);
            if (ambiguous.Success) notes.Add("Ambiguous source marker: " + ambiguous.Value + ". Owner clarification is required; no meaning was invented.");
            var anchor = block.Label?.ToUpperInvariant() ?? Hash(normalized);
            if (block.Label != null && !labels.Add(block.Label)) { anchor += "/" + block.Parts[0].Location; notes.Add("Repeated source label has different content. Resolve this ambiguity before estimation."); }
            var classification = block.Label?.StartsWith("NFR", StringComparison.OrdinalIgnoreCase) == true ? RequirementClassification.NonFunctional : block.Label?.StartsWith("FR", StringComparison.OrdinalIgnoreCase) == true ? RequirementClassification.Functional : RequirementClassification.Unclassified;
            if (classification != RequirementClassification.Unclassified) notes.Add("Classification is inferred from the FR/NFR source label and requires owner confirmation.");
            normalizedBodies.Add(normalized, items.Count);
            items.Add(new("req-" + Hash(projectId + "/" + documentKey + "/" + anchor)[..24], body, [], null, classification, [], RequirementComplexity.Unknown,
                RequirementImplementationState.NotStarted, [], null, [], "", [source], notes.ToArray()));
        }
        if (previous != null)
            foreach (var absent in previous.Requirements.Where(old => !items.Any(x => x.Id == old.Id)))
                items.Add(absent.ImplementationState == RequirementImplementationState.OutOfScope && absent.OwnerReviewed ? absent : absent with
                {
                    ImplementationState = RequirementImplementationState.Blocked, OwnerReviewed = false,
                    Warnings = [.. absent.Warnings, "Absent from the latest document. Original source retained; owner must reconcile or explicitly exclude."],
                    ReviewNote = "Missing from latest import; not silently removed from project scope."
                });
        if (items.Count is 0 or > RequirementScope.MaximumRequirements) throw new ArgumentException("Document must yield 1–500 requirement candidates.");
        var document = new RequirementDocument(documentKey, fileName, doc.MediaType, sha, doc.Text);
        var revision = Hash(JsonSerializer.Serialize(new { projectId, expectedRevision, document, items }));
        var scope = new RequirementScope(projectId, revision, expectedRevision, (previous?.Version ?? 0) + 1, DateTimeOffset.UtcNow, document, items.ToArray(), warnings.ToArray(), "Document imported for owner review; no model processing or execution authorized.");
        ValidateScopeSize(scope);
        if (!await scopes.AppendScopeAsync(scope, expectedRevision, ct)) throw new InvalidOperationException("Scope changed during import. Reload before retrying.");
        return scope;
    }
    public async Task<RequirementScope> ReviewAsync(string projectId, string expectedRevision, string requirementId, RequirementReview input, CancellationToken ct)
    {
        await ProjectAsync(projectId, ct, true);
        var current = await scopes.CurrentScopeAsync(projectId, ct) ?? throw new KeyNotFoundException("No imported scope.");
        if (current.Revision != expectedRevision) throw new InvalidOperationException("Scope changed. Reload before reviewing.");
        if (input.Description is null || input.AcceptanceCriteria is null || input.Dependencies is null || input.Components is null || input.Roles is null || input.VerificationStrategy is null || input.ReviewNote is null || string.IsNullOrWhiteSpace(input.Description) || input.Description.Length > 8000 || input.AcceptanceCriteria.Length > 20 || input.AcceptanceCriteria.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 1000) || input.Priority is < 1 or > 5 || input.Dependencies.Length > 50 || input.Components.Length > 30 || input.Components.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200) || input.Milestone?.Length > 120 || input.VerificationStrategy.Length > 4000 || input.ReviewNote.Length > 2000 || input.Roles.Length > 9 || input.Roles.Distinct().Count() != input.Roles.Length || input.Roles.Any(x => !Enum.IsDefined(x)) || !Enum.IsDefined(input.Classification) || !Enum.IsDefined(input.Complexity) || !Enum.IsDefined(input.ImplementationState)) throw new ArgumentException("Requirement review is outside supported bounds.");
        if (input.ImplementationState == RequirementImplementationState.Verified) throw new ArgumentException("Owner edits cannot certify a requirement. Verified requires acceptance evidence tied to a source revision.");
        if (input.ImplementationState is RequirementImplementationState.Blocked or RequirementImplementationState.OutOfScope && string.IsNullOrWhiteSpace(input.ReviewNote)) throw new ArgumentException("Blocked or excluded requirements require a reason.");
        var item = current.Requirements.SingleOrDefault(x => x.Id == requirementId) ?? throw new KeyNotFoundException("Requirement is not in this scope.");
        if (input.Dependencies.Any(x => x == requirementId || !current.Requirements.Any(r => r.Id == x)) || input.Dependencies.Distinct().Count() != input.Dependencies.Length) throw new ArgumentException("Dependencies must be distinct requirement IDs in this scope.");
        var replacement = item with { Description = input.Description, AcceptanceCriteria = input.AcceptanceCriteria, Priority = input.Priority, Classification = input.Classification, Dependencies = input.Dependencies, Complexity = input.Complexity, ImplementationState = input.ImplementationState, Components = input.Components, Milestone = input.Milestone, Roles = input.Roles, VerificationStrategy = input.VerificationStrategy, ReviewNote = input.ReviewNote, OwnerReviewed = true };
        var requirements = current.Requirements.Select(x => x.Id == item.Id ? replacement : x).ToArray();
        ValidateDependencies(requirements);
        var revision = Hash(JsonSerializer.Serialize(new { projectId, expectedRevision, requirements }));
        var scope = current with { Revision = revision, ParentRevision = expectedRevision, Version = current.Version + 1, CreatedAt = DateTimeOffset.UtcNow, Requirements = requirements, Reason = "Owner reviewed " + requirementId };
        ValidateScopeSize(scope);
        if (!await scopes.AppendScopeAsync(scope, expectedRevision, ct)) throw new InvalidOperationException("Scope changed during review. Reload before retrying.");
        return scope;
    }
    private static void ValidateScopeSize(RequirementScope scope)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(scope).Length > 2_000_000) throw new ArgumentException("Normalized scope exceeds 2000000 bytes. Split or simplify the document while preserving source references.");
    }
    private static void ValidateDependencies(ScopedRequirement[] items)
    {
        var byId = items.ToDictionary(x => x.Id); var visiting = new HashSet<string>(); var visited = new HashSet<string>();
        void Visit(string id) { if (visited.Contains(id)) return; if (!visiting.Add(id)) throw new ArgumentException("Requirement dependencies contain a cycle."); foreach (var dependency in byId[id].Dependencies) Visit(dependency); visiting.Remove(id); visited.Add(id); }
        foreach (var item in items) Visit(item.Id);
    }
    private static string Normalize(string value) => Regex.Replace(value.Trim(), @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100));
    [GeneratedRegex(@"\b(?:TBD|TODO|TBC|to be decided|as needed)\b", RegexOptions.IgnoreCase, 100)] private static partial Regex AmbiguityMarkers();
    [GeneratedRegex(@"\A[a-zA-Z0-9][a-zA-Z0-9_.-]{0,79}\z")] private static partial Regex DocumentKey();
    [GeneratedRegex(@"^\s*(?:#{1,6}\s+)?(?:(?<label>(?:REQ|NFR|FR|R)[-_]?\d+)[.\s:\-]+|(?<label>\d+)[.)]\s+|[-*]\s+)", RegexOptions.IgnoreCase, 100)] private static partial Regex ItemStart();
}
