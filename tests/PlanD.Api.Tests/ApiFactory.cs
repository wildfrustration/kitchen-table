using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PlanD.Api.Data;
using PlanD.Data;

// Every test class empties and reseeds the same database, so they must not run at the same time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PlanD.Api.Tests;

/// <summary>
/// The real API against a real Postgres database "pland_test" (created next to the database PLAND_DB points at,
/// or the local docker one). App tables are emptied before each test; CMS reference data isn't needed.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "integration-test-password";

    private static readonly string TestConnectionString = new NpgsqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("PLAND_DB") is { Length: > 0 } cs ? cs : Db.DefaultConnectionString) { Database = "pland_test" }.ConnectionString;

    public async Task InitializeAsync()
    {
        var admin = new NpgsqlConnectionStringBuilder(TestConnectionString) { Database = "postgres" }.ConnectionString;
        await using (var conn = new NpgsqlConnection(admin))
        {
            if (await conn.ExecuteScalarAsync<int>("select count(*) from pg_database where datname = 'pland_test'") == 0)
                await conn.ExecuteAsync("create database pland_test");
        }

        // The API reads PLAND_DB when it builds its services.
        Environment.SetEnvironmentVariable("PLAND_DB", TestConnectionString);
        await using var scope = Services.CreateAsyncScope();
        await Migrator.MigrateAsync(scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>());
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => base.DisposeAsync().AsTask();

    /// <summary>Empties the app schema and creates one agency with one broker; returns the broker's email.</summary>
    public async Task<string> ResetWithBrokerAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            truncate app.quote_snapshots, app.patient_links, app.client_notes, app.consents, app.client_pharmacies,
                     app.client_drugs, app.clients, app.broker_carriers, app.broker_invites, app.broker_tokens,
                     app.broker_logins, app.broker_claims, app.brokers, app.agencies cascade
            """);
        var agency = new Agency { Name = "Test Agency" };
        db.Agencies.Add(agency);
        await db.SaveChangesAsync();

        const string email = "broker@test.example.com";
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BrokerUser>>();
        var result = await users.CreateAsync(new BrokerUser
        {
            AgencyId = agency.Id, UserName = email, Email = email, DisplayName = "Test Broker",
            Role = BrokerRole.AgencyAdmin, PublicSlug = "test-broker",
        }, Password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return email;
    }
}
