using System.Text.Json;
using Dapper;
using Npgsql;

namespace PlanD.Data;

public static class ReleaseSource
{
    public const string Spuf = "spuf";
    public const string Landscape = "landscape";
    public const string Geo = "geo";
    public const string RxNorm = "rxnorm";
}

public sealed record Release(int Id, string Source, int? PlanYear, string Label, string Status);

/// <summary>
/// Every load creates a release. It becomes 'ready' when all its tables load and pass their checks,
/// and 'active' when promoted; the previously active release of the same source and year is retired.
/// </summary>
public static class Releases
{
    public static async Task<int> CreateAsync(NpgsqlDataSource db, string source, int? planYear, string label, string sourcePath)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>("""
            insert into cms.release (source, plan_year, label, source_path)
            values (@source, @planYear, @label, @sourcePath)
            returning id
            """, new { source, planYear, label, sourcePath });
    }

    public static async Task MarkReadyAsync(NpgsqlDataSource db, int releaseId, IReadOnlyDictionary<string, long> rowCounts)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update cms.release set status = 'ready', row_counts = @counts::jsonb where id = @releaseId",
            new { releaseId, counts = JsonSerializer.Serialize(rowCounts) });
    }

    public static async Task MarkFailedAsync(NpgsqlDataSource db, int releaseId)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync("update cms.release set status = 'failed' where id = @releaseId", new { releaseId });
    }

    public static async Task ActivateAsync(NpgsqlDataSource db, int releaseId)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var release = await conn.QuerySingleAsync<Release>(
            "select id, source, plan_year as PlanYear, label, status from cms.release where id = @releaseId for update",
            new { releaseId }, tx);
        if (release.Status is not ("ready" or "active"))
            throw new InvalidOperationException($"Release {releaseId} is '{release.Status}'; only a ready release can be activated.");

        await conn.ExecuteAsync("""
            update cms.release set status = 'retired'
            where source = @Source and coalesce(plan_year, 0) = coalesce(@PlanYear, 0) and status = 'active' and id <> @Id
            """, release, tx);
        await conn.ExecuteAsync(
            "update cms.release set status = 'active', activated_at = now() where id = @Id", release, tx);
        await tx.CommitAsync();
    }

    public static async Task<IReadOnlyList<Release>> ListAsync(NpgsqlDataSource db)
    {
        await using var conn = await db.OpenConnectionAsync();
        return (await conn.QueryAsync<Release>(
            "select id, source, plan_year as PlanYear, label, status from cms.release order by id")).ToList();
    }

    /// <summary>The active release id for a source (and plan year, when the source is yearly).</summary>
    public static async Task<int?> ActiveAsync(NpgsqlConnection conn, string source, int? planYear) =>
        await conn.ExecuteScalarAsync<int?>("""
            select id from cms.release
            where source = @source and coalesce(plan_year, 0) = coalesce(@planYear, 0) and status = 'active'
            """, new { source, planYear });
}
