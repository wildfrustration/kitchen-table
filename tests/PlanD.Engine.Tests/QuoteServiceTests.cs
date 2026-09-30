using PlanD.Engine;
using PlanD.Engine.Quoting;

namespace PlanD.Engine.Tests;

public class QuoteServiceTests
{
    [Fact]
    public async Task Ranks_by_premium_plus_drug_cost_not_premium_alone()
    {
        // Cheap plan charges 25% on a $400 drug; pricier plan has a $10 copay.
        var cheap = Offer("S0001", premium: 5m);
        var rich = Offer("S0002", premium: 60m);
        var data = new FakeData()
            .With(cheap, Tiers(3, Coins(0.25m)), Covered(3, 400m))
            .With(rich, Tiers(3, Copay(10m)), Covered(3, 400m));

        var quote = await new QuoteService(data).QuoteAsync(Request());

        Assert.Equal(["S0002", "S0001"], quote.Plans.Select(p => p.Plan.Key.ContractId));
        Assert.Equal(60m * 12 + 10m * 12, quote.Plans[0].EstimatedAnnualCost);
        Assert.Equal(5m * 12 + 100m * 12, quote.Plans[1].EstimatedAnnualCost);
    }

    [Fact]
    public async Task Uses_the_preferred_pharmacy_only_when_the_plan_has_a_preferred_network()
    {
        var withNetwork = Offer("S0001");
        var withoutNetwork = Offer("S0002");
        var tiers = Tiers(1, preferred: Copay(0m), standard: Copay(5m));
        var data = new FakeData()
            .With(withNetwork, tiers, Covered(1, 20m), preferredPharmacies: 100)
            .With(withoutNetwork, tiers, Covered(1, 20m), preferredPharmacies: 0);

        var quote = await new QuoteService(data).QuoteAsync(Request());
        var byPlan = quote.Plans.ToDictionary(p => p.Plan.Key.ContractId);

        Assert.Equal(PharmacyType.PreferredRetail, byPlan["S0001"].Drugs.Pharmacy);
        Assert.Equal(0m, byPlan["S0001"].Drugs.Total);
        Assert.Equal(PharmacyType.StandardRetail, byPlan["S0002"].Drugs.Pharmacy);
        Assert.Equal(60m, byPlan["S0002"].Drugs.Total);
    }

    [Fact]
    public async Task Mail_order_turns_30_day_fills_into_90_day_fills()
    {
        var plan = Offer("S0001");
        var data = new FakeData().With(plan, Tiers(1, Copay(3m)), Covered(1, 10m));

        var quote = await new QuoteService(data).QuoteAsync(Request(pharmacy: PharmacyPreference.Mail));
        var line = quote.Plans[0].Drugs.Drugs[0];

        Assert.Equal(4, line.Fills);
        Assert.Equal(12m, quote.Plans[0].Drugs.Total);
    }

    [Fact]
    public async Task Extra_help_uses_the_low_income_premium()
    {
        var plan = Offer("S0001", premium: 40m) with { LisPremium = 0m };
        var data = new FakeData().With(plan, Tiers(1, Copay(0m)), Covered(1, 10m));

        var quote = await new QuoteService(data).QuoteAsync(Request() with { ExtraHelp = ExtraHelpLevel.Category1 });

        Assert.Equal(0m, quote.Plans[0].MonthlyPremium);
    }

    [Fact]
    public async Task A_drug_off_the_formulary_is_flagged_in_the_notes()
    {
        var plan = Offer("S0001");
        var data = new FakeData().With(plan, Tiers(1, Copay(0m)), drug: null, marketPrice: 50m);

        var quote = await new QuoteService(data).QuoteAsync(Request());

        Assert.Equal(600m, quote.Plans[0].Drugs.Total);
        Assert.Contains(quote.Plans[0].Notes, n => n.Contains("not covered"));
    }

    [Fact]
    public async Task Named_pharmacy_is_priced_with_its_own_preferred_status_and_fees()
    {
        var plan = Offer("S0001");
        var data = new FakeData().With(plan, Tiers(1, preferred: Copay(0m), standard: Copay(5m)), Covered(1, 20m), preferredPharmacies: 100);
        var fee = new DispensingFees([2m, 2m, 2m], [2m, 2m, 2m], [null, null, null]);
        data.Named[(plan.Key, "1111111111")] = new NetworkPharmacy(Retail: true, Mail: false, PreferredRetail: false, PreferredMail: false, fee);

        var quote = await new QuoteService(data).QuoteAsync(Request() with { PharmacyNpis = ["1111111111", "2222222222"] });
        var q = quote.Plans[0];

        // The client's pharmacy is standard, not preferred: $5 copay, even though the plan has preferred pharmacies.
        Assert.Equal(PharmacyType.StandardRetail, q.Drugs.Pharmacy);
        Assert.Equal("1111111111", q.PricedAtNpi);
        Assert.Equal(60m, q.Drugs.Total);
        Assert.Equal(22m, q.Drugs.Drugs[0].FullCostPerFill);
        Assert.True(q.PharmacyInNetwork);
        Assert.Equal([true, false], q.Pharmacies.Select(p => p.InNetwork));
    }

    [Fact]
    public async Task Plan_without_any_of_the_clients_pharmacies_is_flagged()
    {
        var plan = Offer("S0001");
        var data = new FakeData().With(plan, Tiers(1, Copay(1m)), Covered(1, 20m));

        var quote = await new QuoteService(data).QuoteAsync(Request() with { PharmacyNpis = ["3333333333"] });

        Assert.False(quote.Plans[0].PharmacyInNetwork);
        Assert.Null(quote.Plans[0].PricedAtNpi);
        Assert.Contains(quote.Plans[0].Notes, n => n.Contains("None of the client's pharmacies"));
    }

    // --- helpers -------------------------------------------------------------------------------

    private const string Rxcui = "123";

    private static QuoteRequest Request(PharmacyPreference pharmacy = PharmacyPreference.Retail) => new()
    {
        Year = 2026,
        CountyCode = "05200",
        Drugs = [new DrugRequest(Rxcui, 1m, 30)],
        Pharmacy = pharmacy,
    };

    private static PlanOffer Offer(string contract, decimal premium = 0m) => new(
        new PlanKey(contract, "001", "000"), $"Plan {contract}", "Org", DrugBenefitType.EnhancedAlternative, "F1",
        Deductible: 0m, Premium: premium, LisPremium: premium, StarRating: "4.0", LisBenchmark: false);

    private static TierCost[] Tiers(int tier, CostShare share) => Tiers(tier, share, share);

    private static TierCost[] Tiers(int tier, CostShare preferred, CostShare standard)
    {
        var zero = Copay(0m);
        return new[] { 30, 60, 90 }.SelectMany(d => new[]
        {
            new TierCost(CoveragePhase.Initial, tier, d, [preferred, standard, preferred, standard], false, false),
            new TierCost(CoveragePhase.Catastrophic, tier, d, [zero, zero, zero, zero], false, false),
        }).ToArray();
    }

    private static CostShare Copay(decimal amount) => new(CostType.Copay, amount);
    private static CostShare Coins(decimal fraction) => new(CostType.Coinsurance, fraction);

    private static PlanDrug Covered(int tier, decimal unitCost) => new(
        Rxcui, CoverageStatus.Covered, tier,
        UnitCostByDaysSupply: new Dictionary<int, decimal> { [30] = unitCost, [60] = unitCost, [90] = unitCost });

    private sealed class FakeData : IQuoteData
    {
        private readonly List<PlanOffer> _plans = [];
        private readonly Dictionary<PlanKey, PlanBenefit> _benefits = [];
        private readonly Dictionary<(PlanKey, string), PlanDrug> _drugs = [];
        private readonly Dictionary<string, IReadOnlyDictionary<int, decimal>> _market = [];
        private readonly Dictionary<(PlanKey, PharmacyType), NetworkInfo> _networks = [];

        public FakeData With(PlanOffer plan, TierCost[] tiers, PlanDrug? drug, int preferredPharmacies = 0, decimal? marketPrice = null)
        {
            _plans.Add(plan);
            _benefits[plan.Key] = new PlanBenefit(plan.Key, plan.Deductible, plan.BenefitType!.Value, tiers);
            if (drug is not null) _drugs[(plan.Key, drug.Rxcui)] = drug;
            if (marketPrice is { } m) _market[Rxcui] = new Dictionary<int, decimal> { [30] = m, [60] = m, [90] = m };
            var none = new NetworkInfo(0, 0, DispensingFees.None);
            _networks[(plan.Key, PharmacyType.PreferredRetail)] = none with { PharmacyCount = preferredPharmacies };
            _networks[(plan.Key, PharmacyType.StandardRetail)] = none with { PharmacyCount = 1000 };
            _networks[(plan.Key, PharmacyType.PreferredMail)] = none with { PharmacyCount = 1 };
            return this;
        }

        public Task<IReadOnlyList<PlanOffer>> PlansForCountyAsync(int year, string countyCode, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PlanOffer>>(_plans);

        public Task<IReadOnlyList<DrugInfo>> DrugInfoAsync(IReadOnlyCollection<string> rxcuis, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DrugInfo>>(rxcuis.Select(r => new DrugInfo(r, "test drug 10 MG Oral Tablet", "SCD")).ToList());

        public Dictionary<(PlanKey, string), NetworkPharmacy> Named { get; } = [];

        public Task<PlanDrugData> PlanDrugDataAsync(int year, IReadOnlyList<PlanOffer> plans, IReadOnlyCollection<string> rxcuis,
            IReadOnlyCollection<string> pharmacyNpis, CancellationToken ct = default) =>
            Task.FromResult(new PlanDrugData(_benefits, _drugs, _market, _networks) { NamedPharmacies = Named });
    }
}
