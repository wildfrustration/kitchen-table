using Microsoft.AspNetCore.Http.HttpResults;
using PlanD.Data;

namespace PlanD.Api.Endpoints;

/// <summary>Public lookups the patient form and the broker workspace share. No personal data.</summary>
public static class ReferenceEndpoints
{
    public static void MapReferenceEndpoints(this RouteGroupBuilder api)
    {
        var reference = api.MapGroup("/reference").WithTags("Reference");

        reference.MapGet("/zip/{zip}", async Task<Results<Ok<IReadOnlyList<CountyMatch>>, BadRequest<string>>> (
            string zip, QuoteRepository repo, CancellationToken ct) =>
        {
            if (zip.Length != 5 || !zip.All(char.IsAsciiDigit)) return TypedResults.BadRequest("ZIP must be 5 digits.");
            return TypedResults.Ok(await repo.ResolveZipAsync(zip, ct));
        });

        reference.MapGet("/drugs", async (string q, int? year, QuoteRepository repo, CancellationToken ct) =>
            TypedResults.Ok(q.Trim().Length < 2 ? [] : await repo.SearchDrugsAsync(q, year ?? await repo.LatestPlanYearAsync(ct), 20, ct)));

        reference.MapGet("/pharmacies", async Task<Results<Ok<IReadOnlyList<PharmacySearchResult>>, BadRequest<string>>> (
            string zip, string? q, double? radius, QuoteRepository repo, CancellationToken ct) =>
        {
            if (zip.Length != 5 || !zip.All(char.IsAsciiDigit)) return TypedResults.BadRequest("ZIP must be 5 digits.");
            return TypedResults.Ok(await repo.SearchPharmaciesAsync(zip, q, Math.Clamp(radius ?? 15, 1, 100), 20, ct));
        });
    }
}
