using System.Reflection;
using Dapper;
using Npgsql;

namespace PlanD.Data;

public static class Db
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5442;Database=pland;Username=pland;Password=pland;Command Timeout=0;Include Error Detail=true;Gss Encryption Mode=Disable";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("PLAND_DB") is { Length: > 0 } cs ? cs : DefaultConnectionString;

    public static NpgsqlDataSource CreateDataSource(string? connectionString = null) =>
        NpgsqlDataSource.Create(connectionString ?? ConnectionString);
}

/// <summary>Applies the embedded Sql/NNN_*.sql scripts in order, once each.</summary>
public static class Migrator
{
    public static async Task<IReadOnlyList<string>> MigrateAsync(NpgsqlDataSource db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("""
            create table if not exists public.schema_migrations (
                name       text primary key,
                applied_at timestamptz not null default now()
            )
            """);

        var applied = (await conn.QueryAsync<string>("select name from public.schema_migrations")).ToHashSet();
        var assembly = Assembly.GetExecutingAssembly();
        var scripts = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal);

        var ran = new List<string>();
        foreach (var resource in scripts)
        {
            var name = resource[(resource.IndexOf(".Sql.", StringComparison.Ordinal) + 5)..];
            if (applied.Contains(name)) continue;

            await using var stream = assembly.GetManifestResourceStream(resource)!;
            var sql = await new StreamReader(stream).ReadToEndAsync(ct);

            await using var tx = await conn.BeginTransactionAsync(ct);
            await conn.ExecuteAsync(sql, transaction: tx);
            await conn.ExecuteAsync("insert into public.schema_migrations (name) values (@name)", new { name }, tx);
            await tx.CommitAsync(ct);
            ran.Add(name);
        }

        return ran;
    }
}
