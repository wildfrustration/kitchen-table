using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using PlanD.Data;
using PlanD.Engine;
using PlanD.Engine.Quoting;

namespace PlanD.Cli;

/// <summary>
/// <c>validate [dir]</c>: re-runs every persona in validation/personas against the active data and compares
/// each plan's yearly drug cost with the Plan Finder figure captured for it.
/// </summary>
internal static class ValidateCommand
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<int> RunAsync(NpgsqlDataSource db, Args a)
    {
        var dir = a.Positionals.Count > 0 ? a.Positionals[0] : "validation/personas";
        var tolerance = a.Option("--tolerance") is { } t ? decimal.Parse(t) : 1.00m;
        var repo = new QuoteRepository(db);
        var service = new QuoteService(repo);
        int pass = 0, fail = 0, known = 0;

        foreach (var file in Directory.GetFiles(dir, "*.json").Order())
        {
            var persona = JsonSerializer.Deserialize<Persona>(await File.ReadAllTextAsync(file), Json)
                          ?? throw new InvalidDataException($"{file} is empty.");
            var county = persona.County ?? (await repo.ResolveZipAsync(persona.Zip)).FirstOrDefault()?.CountyCode
                         ?? throw new InvalidDataException($"{file}: ZIP {persona.Zip} not found.");

            var quote = await service.QuoteAsync(new QuoteRequest
            {
                Year = persona.Year,
                CountyCode = county,
                Drugs = persona.Drugs.Select(d => new DrugRequest(d.Rxcui, d.Quantity, d.Days)).ToList(),
                Pharmacy = persona.Pharmacy,
                ExtraHelp = persona.ExtraHelp,
            });
            var byPlan = quote.Plans.ToDictionary(p => p.Plan.Key);

            Console.WriteLine($"{Path.GetFileNameWithoutExtension(file)} — {persona.Name}");
            foreach (var e in persona.Expected)
            {
                var key = PlanKey.Parse(e.Plan);
                if (!byPlan.TryGetValue(key, out var q))
                {
                    Console.WriteLine($"  FAIL   {key}: plan not in our quote for county {county}");
                    fail++;
                    continue;
                }

                var diff = q.Drugs.Total - e.DrugCostYear;
                var status = Math.Abs(diff) <= tolerance ? "ok" : e.KnownGap is not null ? "KNOWN" : "FAIL";
                Console.WriteLine($"  {status,-6} {key}: ours {q.Drugs.Total,10:C}  Plan Finder {e.DrugCostYear,10:C}  diff {diff,9:+$0.00;-$0.00;$0.00}"
                                  + (status == "KNOWN" ? $"  ({e.KnownGap})" : ""));
                switch (status)
                {
                    case "ok": pass++; break;
                    case "KNOWN": known++; break;
                    default: fail++; break;
                }
            }
        }

        Console.WriteLine($"\n{pass} within {tolerance:C}, {known} known gaps, {fail} failing");
        return fail == 0 ? 0 : 1;
    }

    private sealed record Persona(
        string Name,
        int Year,
        string Zip,
        string? County,
        PharmacyPreference Pharmacy,
        ExtraHelpLevel ExtraHelp,
        IReadOnlyList<PersonaDrug> Drugs,
        IReadOnlyList<Expectation> Expected,
        string? Source,
        string? CapturedOn);

    private sealed record PersonaDrug(string Rxcui, decimal Quantity, int Days = 30, string? Label = null);

    /// <summary>Plan Finder's estimated yearly drug cost for one plan (premium excluded).</summary>
    private sealed record Expectation(string Plan, decimal DrugCostYear, string? KnownGap = null);
}
