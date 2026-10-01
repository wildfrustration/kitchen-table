using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlanD.Api.Data;

namespace PlanD.Api;

/// <summary>
/// <c>demo-reset</c>: wipes every agency, broker and client and loads made-up demo data.
/// Never point this at a database with real clients.
/// </summary>
public static class DemoSeed
{
    public const string Password = "kitchen-table-demo";

    private sealed record SeedBroker(string Email, string Name, BrokerRole Role, string Slug, string[] Carriers);

    private sealed record SeedDrug(string Name, decimal UnitsPerDose, decimal DosesPerDay, int Days = 30);

    private sealed record SeedClient(
        string Broker, string First, string Last, string Zip, int BirthMonth, int BirthYear, MedicareStatus Medicare,
        ExtraHelpAnswer ExtraHelp, bool Mail, ClientStatus Status, SeedDrug[] Drugs);

    private static readonly (string Agency, SeedBroker[] Brokers)[] Agencies =
    [
        ("Sunshine Senior Benefits",
        [
            new("maria@sunshine.example.com", "Maria Alvarez", BrokerRole.AgencyAdmin, "maria-alvarez",
                ["Humana Inc.", "UnitedHealth Group, Inc.", "Centene Corporation", "CVS Health Corporation"]),
            new("james@sunshine.example.com", "James Chen", BrokerRole.Broker, "james-chen",
                ["Humana Inc.", "Centene Corporation"]),
        ]),
        ("Carolina Medicare Help",
        [
            new("denise@carolina.example.com", "Denise Porter", BrokerRole.AgencyAdmin, "denise-porter",
                ["Humana Inc.", "UnitedHealth Group, Inc.", "CVS Health Corporation", "Centene Corporation"]),
        ]),
    ];

    private static readonly SeedDrug Atorvastatin = new("atorvastatin 20 MG Oral Tablet", 1, 1);
    private static readonly SeedDrug Eliquis = new("apixaban 5 MG Oral Tablet [Eliquis]", 1, 2);
    private static readonly SeedDrug Metformin = new("metformin hydrochloride 500 MG Oral Tablet", 1, 2);
    private static readonly SeedDrug Lisinopril = new("lisinopril 10 MG Oral Tablet", 1, 1);
    private static readonly SeedDrug Amlodipine = new("amlodipine 5 MG Oral Tablet", 1, 1);
    private static readonly SeedDrug Levothyroxine = new("levothyroxine sodium 0.05 MG Oral Tablet", 1, 1);
    private static readonly SeedDrug Jardiance = new("empagliflozin 10 MG Oral Tablet [Jardiance]", 1, 1);
    private static readonly SeedDrug Omeprazole = new("omeprazole 20 MG Delayed Release Oral Capsule", 1, 1);
    private static readonly SeedDrug Lantus = new("3 ML insulin glargine 100 UNT/ML Pen Injector [Lantus]", 0.3m, 1);
    private static readonly SeedDrug Trulicity = new("0.5 ML dulaglutide 1.5 MG/ML Auto-Injector [Trulicity]", 0.5m, 1m / 7);
    private static readonly SeedDrug Xarelto = new("rivaroxaban 20 MG Oral Tablet [Xarelto]", 1, 1);
    private static readonly SeedDrug Losartan = new("losartan potassium 50 MG Oral Tablet", 1, 1);
    private static readonly SeedDrug Sertraline = new("sertraline 50 MG Oral Tablet", 1, 1);
    private static readonly SeedDrug Gabapentin = new("gabapentin 300 MG Oral Capsule", 1, 3);
    private static readonly SeedDrug Tamsulosin = new("tamsulosin hydrochloride 0.4 MG Oral Capsule", 1, 1);
    private static readonly SeedDrug Donepezil = new("donepezil hydrochloride 10 MG Oral Tablet", 1, 1);
    private static readonly SeedDrug Rosuvastatin = new("rosuvastatin calcium 10 MG Oral Tablet", 1, 1);

    private static readonly SeedClient[] Clients =
    [
        new("maria-alvarez", "Rosa", "Delgado", "33135", 3, 1958, MedicareStatus.OnMedicare, ExtraHelpAnswer.No, false, ClientStatus.New,
            [Atorvastatin, Eliquis, Metformin]),
        new("maria-alvarez", "Harold", "Feldman", "33139", 11, 1961, MedicareStatus.TurningSixtyFive, ExtraHelpAnswer.No, true, ClientStatus.New,
            [Lisinopril, Rosuvastatin, Tamsulosin]),
        new("maria-alvarez", "Lucia", "Moreno", "33012", 6, 1952, MedicareStatus.OnMedicare, ExtraHelpAnswer.Medicaid, false, ClientStatus.Reviewed,
            [Lantus, Metformin, Losartan, Gabapentin]),
        new("james-chen", "Walter", "Brooks", "33410", 1, 1949, MedicareStatus.OnMedicare, ExtraHelpAnswer.No, false, ClientStatus.New,
            [Xarelto, Donepezil, Omeprazole]),
        new("james-chen", "Ellen", "Park", "32801", 8, 1960, MedicareStatus.OnMedicare, ExtraHelpAnswer.NotSure, false, ClientStatus.Contacted,
            [Levothyroxine, Sertraline]),
        new("denise-porter", "Gerald", "Whitfield", "27215", 4, 1955, MedicareStatus.OnMedicare, ExtraHelpAnswer.No, false, ClientStatus.New,
            [Trulicity, Jardiance, Atorvastatin, Lisinopril]),
        new("denise-porter", "Betty", "Lawson", "27401", 9, 1947, MedicareStatus.OnMedicare, ExtraHelpAnswer.ExtraHelp, false, ClientStatus.New,
            [Eliquis, Amlodipine, Omeprazole, Donepezil]),
        new("denise-porter", "Samuel", "Greene", "28202", 12, 1961, MedicareStatus.TurningSixtyFive, ExtraHelpAnswer.No, true, ClientStatus.Reviewed,
            [Atorvastatin, Metformin]),
        new("denise-porter", "Carol", "Nguyen", "27601", 2, 1956, MedicareStatus.OnMedicare, ExtraHelpAnswer.No, false, ClientStatus.Enrolled,
            [Levothyroxine, Losartan, Rosuvastatin]),
        new("denise-porter", "Frank", "Russo", "27514", 7, 1950, MedicareStatus.OnMedicare, ExtraHelpAnswer.No, false, ClientStatus.Closed,
            [Gabapentin, Sertraline, Tamsulosin]),
    ];

    /// <summary><c>demo-seed</c>: loads the demo data only when there are no agencies yet (a fresh database).</summary>
    public static async Task SeedIfEmptyAsync(IServiceProvider root)
    {
        await using (var scope = root.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.Agencies.AnyAsync())
            {
                Console.WriteLine("App data exists; demo seed skipped.");
                return;
            }
            if (!await db.Database.SqlQueryRaw<int>("select count(*)::int as \"Value\" from cms.release where source = 'rxnorm' and status = 'active'").AnyAsync(n => n > 0))
            {
                Console.WriteLine("No CMS data loaded yet; demo seed skipped (restore data/dumps/cms.dump first).");
                return;
            }
        }
        await ResetAsync(root);
    }

    public static async Task ResetAsync(IServiceProvider root)
    {
        await using var scope = root.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var users = sp.GetRequiredService<UserManager<BrokerUser>>();
        var rxcuis = await ResolveDrugs(sp.GetRequiredService<NpgsqlDataSource>());
        var pharmacies = await NearbyPharmacies(sp.GetRequiredService<PlanD.Data.QuoteRepository>());

        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("""
            truncate app.quote_snapshots, app.patient_links, app.client_notes, app.consents, app.client_pharmacies,
                     app.client_drugs, app.clients, app.broker_carriers, app.broker_invites, app.broker_tokens,
                     app.broker_logins, app.broker_claims, app.brokers, app.agencies cascade
            """);

        var brokers = new Dictionary<string, BrokerUser>();
        foreach (var (agencyName, seedBrokers) in Agencies)
        {
            var agency = new Agency { Name = agencyName };
            db.Agencies.Add(agency);
            await db.SaveChangesAsync();

            foreach (var b in seedBrokers)
            {
                var user = new BrokerUser
                {
                    AgencyId = agency.Id, UserName = b.Email, Email = b.Email, EmailConfirmed = true,
                    DisplayName = b.Name, Role = b.Role, PublicSlug = b.Slug, PhoneNumber = "(555) 010-0100",
                };
                var created = await users.CreateAsync(user, Password);
                if (!created.Succeeded) throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
                db.BrokerCarriers.AddRange(b.Carriers.Select(c => new BrokerCarrier { BrokerId = user.Id, ParentOrganization = c }));
                brokers[b.Slug] = user;
            }
        }

        var now = DateTimeOffset.UtcNow;
        var i = 0;
        foreach (var c in Clients)
        {
            var broker = brokers[c.Broker];
            var submitted = now.AddHours(-6 * ++i);
            db.Clients.Add(new Client
            {
                AgencyId = broker.AgencyId, BrokerId = broker.Id, Status = c.Status,
                Source = i % 3 == 0 ? ClientSource.Invite : ClientSource.PublicLink,
                FirstName = c.First, LastName = c.Last, Email = $"{c.First.ToLowerInvariant()}.{c.Last.ToLowerInvariant()}@example.com",
                Phone = "(555) 010-01" + i.ToString("00"), Zip = c.Zip, BirthMonth = c.BirthMonth, BirthYear = c.BirthYear,
                MedicareStatus = c.Medicare, ExtraHelp = c.ExtraHelp,
                ExtraHelpLevel = c.ExtraHelp is ExtraHelpAnswer.Medicaid ? 2 : c.ExtraHelp is ExtraHelpAnswer.ExtraHelp ? 1 : 0,
                UsesMailOrder = c.Mail, CreatedAt = submitted, UpdatedAt = submitted, SubmittedAt = submitted,
                SeenByBrokerAt = c.Status == ClientStatus.New ? null : submitted.AddHours(1),
                Drugs = c.Drugs.Select((d, n) => new ClientDrug
                {
                    Rxcui = rxcuis[d.Name], Name = d.Name, UnitsPerDose = d.UnitsPerDose, DosesPerDay = d.DosesPerDay,
                    DaysSupply = d.Days, SortOrder = n,
                }).ToList(),
                Pharmacies = pharmacies.TryGetValue(c.Zip, out var ph)
                    ? [new ClientPharmacy { Npi = ph.Npi, Name = ph.Name, Address = ph.Address, Zip = ph.Zip }]
                    : [],
                Consents =
                [
                    new Consent { Kind = ConsentKind.Contact, TextVersion = "demo-1", GrantedAt = submitted },
                    new Consent { Kind = ConsentKind.ShareHealthInfo, TextVersion = "demo-1", GrantedAt = submitted },
                ],
            });
        }
        await db.SaveChangesAsync();

        Console.WriteLine($"Demo data loaded: {brokers.Count} brokers, {Clients.Length} clients. Password for every broker: {Password}");
        foreach (var (_, b) in brokers) Console.WriteLine($"  {b.Email}  ({b.DisplayName}, {b.Role}, /start/{b.PublicSlug})");
    }

    /// <summary>The nearest in-network Walgreens or CVS to each seed ZIP, so demo quotes show pharmacy status.</summary>
    private static async Task<Dictionary<string, (string Npi, string Name, string? Address, string? Zip)>> NearbyPharmacies(PlanD.Data.QuoteRepository repo)
    {
        var found = new Dictionary<string, (string, string, string?, string?)>();
        foreach (var zip in Clients.Select(c => c.Zip).Distinct())
        foreach (var chain in new[] { "walgreens", "cvs" })
        {
            var hit = (await repo.SearchPharmaciesAsync(zip, chain, 10))
                .FirstOrDefault(p => p.InNetwork && !p.MailOrder && !p.Name.Contains("SPECIALTY", StringComparison.OrdinalIgnoreCase));
            if (hit is null) continue;
            var name = Title(hit.Name).Replace("Cvs", "CVS");
            var address = string.Join(", ", new[] { hit.Address1, hit.City }.Where(s => s is not null).Select(s => Title(s!)));
            found[zip] = (hit.Npi, name, address, hit.Zip);
            break;
        }
        return found;
    }

    /// <summary>"4451 W 12TH AVE" → "4451 W 12th Ave": capitalize words that start with a letter.</summary>
    private static string Title(string s) => string.Join(' ', s.ToLowerInvariant().Split(' ')
        .Select(w => w is "n" or "s" or "e" or "w" or "ne" or "nw" or "se" or "sw" or "us" or "po" ? w.ToUpperInvariant()
            : w.Length > 0 && char.IsLetter(w[0]) ? char.ToUpperInvariant(w[0]) + w[1..] : w));

    /// <summary>Seed drugs by their exact RxNorm name so a new RxNorm release can't silently change them.</summary>
    private static async Task<Dictionary<string, string>> ResolveDrugs(NpgsqlDataSource cms)
    {
        var names = Clients.SelectMany(c => c.Drugs).Select(d => d.Name).Distinct().ToArray();
        await using var conn = await cms.OpenConnectionAsync();
        var found = (await conn.QueryAsync<(string Name, string Rxcui)>("""
            select c.name, c.rxcui from cms.rx_concept c
            join cms.release r on r.id = c.release_id and r.source = 'rxnorm' and r.status = 'active'
            where c.name = any(@names)
            """, new { names })).ToDictionary(x => x.Name, x => x.Rxcui);
        var missing = names.Where(n => !found.ContainsKey(n)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException($"Seed drugs not in RxNorm: {string.Join("; ", missing)}");
        return found;
    }
}
