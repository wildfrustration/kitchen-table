using System.Globalization;
using Npgsql;
using PlanD.Data;
using PlanD.Engine;
using PlanD.Engine.Quoting;

namespace PlanD.Cli;

/// <summary><c>quote</c>: rank the plans in a ZIP's county for a drug list and print the shortlist.</summary>
internal static class QuoteCommand
{
    public static async Task<int> RunAsync(NpgsqlDataSource db, Args a)
    {
        var repo = new QuoteRepository(db);
        var year = a.Int("--year");
        var zip = a.Option("--zip") ?? throw new ArgumentException("Missing --zip.");

        var counties = await repo.ResolveZipAsync(zip);
        if (counties.Count == 0) throw new ArgumentException($"ZIP {zip} isn't in the crosswalk.");
        var county = a.Option("--county") is { } chosen
            ? counties.FirstOrDefault(c => c.CountyCode == chosen) ?? throw new ArgumentException($"County {chosen} isn't in ZIP {zip}.")
            : counties[0];
        if (counties.Count > 1 && a.Option("--county") is null)
            Console.WriteLine($"ZIP {zip} spans {counties.Count} counties ({string.Join(", ", counties.Select(c => $"{c.CountyCode} {c.CountyName} {c.Share:P0}"))}); using {county.CountyName}. Pass --county to pick another.\n");

        var request = new QuoteRequest
        {
            Year = year,
            CountyCode = county.CountyCode,
            Drugs = a.Options("--drug").Select(ParseDrug).ToList(),
            Pharmacy = a.Flag("--mail") ? PharmacyPreference.Mail : PharmacyPreference.Retail,
            ExtraHelp = a.Option("--extra-help") switch
            {
                null => ExtraHelpLevel.None,
                "1" => ExtraHelpLevel.Category1,
                "2" => ExtraHelpLevel.Category2,
                "3" => ExtraHelpLevel.Category3,
                var x => throw new ArgumentException($"--extra-help must be 1, 2 or 3 (got {x})."),
            },
            StartMonth = a.IntOr("--start-month", 1),
        };
        if (request.Drugs.Count == 0) throw new ArgumentException("Add at least one --drug <rxcui>:<qty>[:<days>].");

        var quote = await new QuoteService(repo).QuoteAsync(request);
        if (a.Options("--plan") is { Count: > 0 } only)
        {
            var keys = only.Select(PlanKey.Parse).ToHashSet();
            quote = quote with { Plans = quote.Plans.Where(p => keys.Contains(p.Plan.Key)).ToList() };
        }
        Print(quote, county, a.IntOr("--top", 15), a.IntOr("--detail", 3));
        return 0;
    }

    private static DrugRequest ParseDrug(string value)
    {
        var parts = value.Split(':');
        if (parts.Length is < 2 or > 3) throw new ArgumentException($"--drug {value}: expected <rxcui>:<qty>[:<days>].");
        return new DrugRequest(parts[0], decimal.Parse(parts[1], CultureInfo.InvariantCulture), parts.Length == 3 ? int.Parse(parts[2]) : 30);
    }

    private static void Print(Quote quote, CountyMatch county, int top, int detail)
    {
        var r = quote.Request;
        Console.WriteLine($"{r.Year} Part D plans in {county.CountyName} ({county.CountyCode}) — {quote.Plans.Count} plans, "
                          + $"{(r.Pharmacy == PharmacyPreference.Mail ? "mail order" : "retail")}, "
                          + $"{(r.ExtraHelp == ExtraHelpLevel.None ? "no Extra Help" : $"Extra Help {r.ExtraHelp}")}, months {r.StartMonth}–12");
        Console.WriteLine("Drugs: " + string.Join("; ", quote.Drugs.Select(d => $"{d.Name} [{d.Rxcui}, {d.Kind}]")));
        Console.WriteLine();
        Console.WriteLine($"{"#",3}  {"Plan",-15} {"Premium/mo",10} {"Drugs/yr",10} {"Total/yr",10} {"Deduct.",8} {"Stars",5}  Name");

        var i = 0;
        foreach (var q in quote.Plans.Take(top))
        {
            i++;
            var p = q.Plan;
            Console.WriteLine($"{i,3}  {p.Key,-15} {q.MonthlyPremium,10:C} {q.Drugs.Total,10:C} "
                              + $"{q.EstimatedAnnualCost,10:C} {p.Deductible,8:C0} {p.StarRating ?? "",5}  {Trim(p.PlanName, 60)}");
        }

        foreach (var q in quote.Plans.Take(Math.Min(detail, top)))
        {
            Console.WriteLine();
            Console.WriteLine($"{q.Plan.Key} {q.Plan.PlanName} — {q.EstimatedAnnualCost:C}/yr");
            foreach (var d in q.Drugs.Drugs)
                Console.WriteLine($"    {Trim(d.Name, 50),-50} tier {d.Tier?.ToString() ?? "-",-2} {d.Status,-20} {d.Fills,2} fills × {d.FullCostPerFill,9:C} full price → you pay {d.MemberCost,9:C}");
            Console.WriteLine($"    by month: {string.Join(" ", q.Drugs.ByMonth.Select(m => m.ToString("0.##", CultureInfo.InvariantCulture)))}");
            foreach (var note in q.Notes) Console.WriteLine($"    - {note}");
        }
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
