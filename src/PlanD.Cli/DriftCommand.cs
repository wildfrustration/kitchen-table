using System.Globalization;
using Npgsql;
using PlanD.Data;
using PlanD.Data.Ingest;
using PlanD.Engine;
using PlanD.Engine.Quoting;

namespace PlanD.Cli;

/// <summary>
/// <c>drift --old &lt;extracted SPUF dir&gt; --new &lt;extracted SPUF dir&gt;</c>: how far off a quote is when it prices
/// a year's plans with the previous year's drug prices (what's published during AEP) instead of the actual ones.
/// Plan benefits and formularies come from the active release; only unit costs are swapped.
/// </summary>
internal static class DriftCommand
{
    private sealed record Persona(string Name, (string Rxcui, decimal Quantity)[] Drugs);

    private static readonly Persona[] Personas =
    [
        new("Generics only (statin, ACE inhibitor, metformin)", [("617310", 30), ("314076", 30), ("861007", 60)]),
        new("Eliquis + atorvastatin", [("1364447", 60), ("617310", 30)]),
        new("Jardiance + metformin + Trulicity", [("1545664", 30), ("861007", 60), ("1551300", 2)]),
        new("Lantus pens + metformin", [("847232", 9), ("861007", 60)]),
        new("Entresto + Eliquis + furosemide", [("1656356", 60), ("1364447", 60), ("310429", 30)]),
        new("Xarelto + levothyroxine + omeprazole", [("1232088", 30), ("966221", 30), ("198051", 30)]),
    ];

    private static readonly string[] Zips = ["33135", "27215", "78701", "90012", "10001", "60614"];

    public static async Task<int> RunAsync(NpgsqlDataSource db, Args a)
    {
        var year = a.IntOr("--year", 2026);
        var rxcuis = Personas.SelectMany(p => p.Drugs.Select(d => d.Rxcui)).ToHashSet();
        var actual = Prices.Load(a.Option("--new") ?? throw new ArgumentException("Missing --new <dir>."), rxcuis);
        var stale = Prices.Load(a.Option("--old") ?? throw new ArgumentException("Missing --old <dir>."), rxcuis);
        Console.WriteLine($"Prices: actual {actual.ByPlan.Count:N0} plan-drug pairs, stand-in {stale.ByPlan.Count:N0}.\n");

        var repo = new QuoteRepository(db);
        var actualQuotes = new QuoteService(new PriceOverride(repo, actual, actual));
        var staleQuotes = new QuoteService(new PriceOverride(repo, stale, stale));

        Console.WriteLine($"{"Client",-46} {"ZIP",5} {"plans",5} {"median $ off",12} {"max $ off",10} {"median % of total",17} {"same #1?",8}");
        var allPct = new List<decimal>();
        foreach (var persona in Personas)
        foreach (var zip in Zips)
        {
            var county = (await repo.ResolveZipAsync(zip)).First().CountyCode;
            var request = new QuoteRequest
            {
                Year = year,
                CountyCode = county,
                Drugs = persona.Drugs.Select(d => new DrugRequest(d.Rxcui, d.Quantity, 30)).ToList(),
            };
            var real = (await actualQuotes.QuoteAsync(request)).Plans;
            var est = (await staleQuotes.QuoteAsync(request)).Plans.ToDictionary(p => p.Plan.Key);

            var diffs = real.Select(p => (Abs: Math.Abs(est[p.Plan.Key].Drugs.Total - p.Drugs.Total),
                Pct: p.EstimatedAnnualCost == 0 ? 0 : Math.Abs(est[p.Plan.Key].EstimatedAnnualCost - p.EstimatedAnnualCost) / p.EstimatedAnnualCost)).ToList();
            allPct.AddRange(diffs.Select(d => d.Pct));
            var bestActual = real[0].Plan.Key;
            var bestEstimate = est.Values.OrderBy(p => p.EstimatedAnnualCost).First().Plan.Key;

            Console.WriteLine($"{Trim(persona.Name, 46),-46} {zip,5} {real.Count,5} {Median(diffs.Select(d => d.Abs)),12:C0} {diffs.Max(d => d.Abs),10:C0} "
                              + $"{Median(diffs.Select(d => d.Pct)),17:P1} {(bestActual == bestEstimate ? "yes" : "NO"),8}");
        }

        var sorted = allPct.Order().ToList();
        Console.WriteLine($"\nAll plan quotes: median error {Median(sorted):P1} of the yearly total; 90th percentile {sorted[(int)(sorted.Count * 0.9)]:P1}; worst {sorted[^1]:P1}");
        return 0;
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var v = values.Order().ToList();
        return v.Count == 0 ? 0 : v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    /// <summary>Unit costs from one extracted SPUF release, by plan and RXCUI, with a market median per RXCUI.</summary>
    private sealed class Prices
    {
        public Dictionary<(PlanKey, string), Dictionary<int, decimal>> ByPlan { get; } = [];
        public Dictionary<string, Dictionary<int, decimal>> Market { get; } = [];

        public static Prices Load(string dir, HashSet<string> rxcuis)
        {
            var files = Directory.GetFiles(dir, "*.txt");
            string File(string pattern) => files.Single(f => Path.GetFileName(f).Contains(pattern, StringComparison.OrdinalIgnoreCase));

            var ndcToRx = new Dictionary<string, string>();
            using (var r = new DelimitedReader(File("basic drugs formulary"), '|'))
            {
                int rx = r.Column("RXCUI"), ndc = r.Column("NDC");
                while (r.Read())
                    if (rxcuis.Contains(r.Required(rx))) ndcToRx[r.Required(ndc)] = r.Required(rx);
            }

            var prices = new Prices();
            var market = new Dictionary<(string, int), List<decimal>>();
            using (var r = new DelimitedReader(File("pricing"), '|'))
            {
                int contract = r.Column("CONTRACT_ID"), plan = r.Column("PLAN_ID"), segment = r.Column("SEGMENT_ID"),
                    ndc = r.Column("NDC"), days = r.Column("DAYS_SUPPLY"), cost = r.Column("UNIT_COST");
                while (r.Read())
                {
                    if (!SpufLoader.IsPdp(r.Span(contract)) || !ndcToRx.TryGetValue(r.Required(ndc), out var rx)) continue;
                    var key = (new PlanKey(r.Required(contract), r.Required(plan), r.Required(segment)), rx);
                    var d = r.Int(days)!.Value;
                    var c = r.Decimal(cost)!.Value;
                    if (!prices.ByPlan.TryGetValue(key, out var byDays)) prices.ByPlan[key] = byDays = [];
                    byDays[d] = c;
                    if (!market.TryGetValue((rx, d), out var list)) market[(rx, d)] = list = [];
                    list.Add(c);
                }
            }
            foreach (var ((rx, d), list) in market)
            {
                if (!prices.Market.TryGetValue(rx, out var byDays)) prices.Market[rx] = byDays = [];
                byDays[d] = Median(list);
            }
            return prices;
        }
    }

    /// <summary>Same plans and benefits, different unit costs: the plan's own price, else the market median.</summary>
    private sealed class PriceOverride(IQuoteData inner, Prices prices, Prices fallback) : IQuoteData
    {
        public Task<IReadOnlyList<PlanOffer>> PlansForCountyAsync(int year, string countyCode, CancellationToken ct = default) =>
            inner.PlansForCountyAsync(year, countyCode, ct);

        public Task<IReadOnlyList<DrugInfo>> DrugInfoAsync(IReadOnlyCollection<string> rxcuis, CancellationToken ct = default) =>
            inner.DrugInfoAsync(rxcuis, ct);

        public async Task<PlanDrugData> PlanDrugDataAsync(int year, IReadOnlyList<PlanOffer> plans, IReadOnlyCollection<string> rxcuis,
            IReadOnlyCollection<string> pharmacyNpis, CancellationToken ct = default)
        {
            var data = await inner.PlanDrugDataAsync(year, plans, rxcuis, pharmacyNpis, ct);
            var drugs = data.Drugs.ToDictionary(kv => kv.Key, kv =>
            {
                var unit = prices.ByPlan.GetValueOrDefault(kv.Key) ?? fallback.Market.GetValueOrDefault(kv.Key.Rxcui);
                return unit is null ? kv.Value : kv.Value with { UnitCostByDaysSupply = unit, UnitCostIsFallback = false };
            });
            return data with { Drugs = drugs };
        }
    }
}
