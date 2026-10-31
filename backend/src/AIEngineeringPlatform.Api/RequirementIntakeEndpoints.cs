using AIEngineeringPlatform.Application.Estimation;
using AIEngineeringPlatform.Domain;
namespace AIEngineeringPlatform.Api;

public static class RequirementIntakeEndpoints
{
    public static void MapRequirementIntake(this RouteGroupBuilder api)
    {
        api.MapGet("/projects/{id}/requirement-scopes/current", async (string id, RequirementIntake intake, CancellationToken ct) =>
        {
            var scope = await intake.CurrentAsync(id, ct);
            if (scope == null) return (IResult)Results.Text("null", "application/json");
            return Results.Json(scope);
        });
        api.MapGet("/projects/{id}/requirement-scopes/{revision}", (string id, string revision, RequirementIntake intake, CancellationToken ct) => intake.GetAsync(id, revision, ct));
        api.MapGet("/projects/{id}/requirement-scopes", (string id, string? before, RequirementIntake intake, CancellationToken ct) => intake.HistoryAsync(id, before, ct));
        api.MapPost("/projects/{id}/requirement-scopes/import", async (string id, string documentKey, string fileName, string? expectedRevision, HttpContext context, RequirementIntake intake, CancellationToken ct) =>
        {
            if (context.Request.ContentType?.Split(';')[0] != "application/octet-stream") throw new ArgumentException("Upload must use application/octet-stream.");
            if (context.Request.ContentLength is > RequirementIntake.MaximumUploadBytes) throw new ArgumentException("Document exceeds 750000 bytes.");
            using var bytes = new MemoryStream(); var chunk = new byte[8192]; int read;
            while ((read = await context.Request.Body.ReadAsync(chunk, ct)) > 0)
            {
                if (bytes.Length + read > RequirementIntake.MaximumUploadBytes) throw new ArgumentException("Document exceeds 750000 bytes.");
                bytes.Write(chunk, 0, read);
            }
            var scope = await intake.ImportAsync(id, documentKey, fileName, bytes.ToArray(), expectedRevision, ct);
            return Results.Created($"/api/v1/projects/{id}/requirement-scopes/{scope.Revision}", scope);
        });
        api.MapPost("/projects/{id}/requirement-scopes/{revision}/requirements/{requirementId}/review", (string id, string revision, string requirementId, RequirementReview input, RequirementIntake intake, CancellationToken ct) => intake.ReviewAsync(id, revision, requirementId, input, ct));
    }
}
