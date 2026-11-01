namespace AIEngineeringPlatform.Domain;

public enum RequirementImplementationState { NotStarted, PartiallyImplemented, ImplementedUnverified, Verified, Blocked, OutOfScope }
public enum RequirementClassification { Unclassified, Functional, NonFunctional }
public enum RequirementComplexity { Unknown, Small, Medium, Large, ExtraLarge }
public sealed record RequirementSource(string DocumentSha256, string Location, string? OriginalLabel, string Text);
public sealed record ScopedRequirement(
    string Id, string Description, string[] AcceptanceCriteria, int? Priority,
    RequirementClassification Classification, string[] Dependencies, RequirementComplexity Complexity,
    RequirementImplementationState ImplementationState, string[] Components, string? Milestone,
    AgentRole[] Roles, string VerificationStrategy, RequirementSource[] Sources,
    string[] Warnings, bool OwnerReviewed = false, string ReviewNote = "");
public sealed record RequirementDocument(string Key, string FileName, string MediaType, string Sha256, string ExtractedText);
public sealed record RequirementScope(
    string ProjectId, string Revision, string? ParentRevision, int Version, DateTimeOffset CreatedAt,
    RequirementDocument Document, ScopedRequirement[] Requirements, string[] Warnings, string Reason)
{
    public const int MaximumRequirements = 500;
    public const int MaximumVersions = 500;
    public bool ReadyForEstimation => Requirements.Length > 0 && Requirements.All(x => x.OwnerReviewed &&
        (x.ImplementationState == RequirementImplementationState.OutOfScope || x.AcceptanceCriteria.Length > 0));
}
