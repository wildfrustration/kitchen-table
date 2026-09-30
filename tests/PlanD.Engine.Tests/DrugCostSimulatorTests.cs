using PlanD.Engine;

namespace PlanD.Engine.Tests;

public class DrugCostSimulatorTests
{
    private static readonly SimulationOptions Year2026 = new() { Parameters = BenefitYearParameters.Cy2026 };

    [Fact]
    public void Deductible_then_coinsurance_with_the_fill_that_crosses_the_deductible_split()
    {
        // Defined standard: $615 deductible on tier 3, then 25%. $100 per fill.
        var plan = Plan(615m, DrugBenefitType.DefinedStandard, Tier(3, Coins(0.25m), deductible: true));
        var result = Run(plan, Drug(price: 100m, tier: 3));

        // Jan–Jun pay $100 each (deductible left: $15), Jul pays $15 + 25% of $85, Aug–Dec $25 each.
        Assert.Equal(600m + 36.25m + 125m, result.Total);
        Assert.Equal(7, result.DeductibleMetMonth);
        Assert.Null(result.CatastrophicMonth);
    }

    [Fact]
    public void Member_pays_nothing_after_reaching_the_out_of_pocket_cap()
    {
        var plan = Plan(615m, DrugBenefitType.DefinedStandard, Tier(5, Coins(0.25m), deductible: true));
        var result = Run(plan, Drug(price: 1500m, tier: 5));

        // Jan $615 + 25% of $885 = $836.25; Feb–Apr $375; May only the $138.75 left to the $2,100 cap.
        Assert.Equal(2100m, result.Total);
        Assert.Equal(5, result.CatastrophicMonth);
        Assert.Equal(138.75m, result.ByMonth[4]);
        Assert.All(result.ByMonth.Skip(5), m => Assert.Equal(0m, m));
    }

    [Fact]
    public void Enhanced_plan_credits_the_standard_benefit_share_toward_the_cap()
    {
        // $47 copay on a $1,000 drug, no deductible.
        var tier = Tier(3, Copay(47m));
        var enhanced = Run(Plan(0m, DrugBenefitType.EnhancedAlternative, tier), Drug(price: 1000m, tier: 3));
        var equivalent = Run(Plan(0m, DrugBenefitType.ActuariallyEquivalent, tier), Drug(price: 1000m, tier: 3));

        // Enhanced: TrOOP grows by the standard share ($711.25, then $250 a fill) and hits $2,100 in July.
        Assert.Equal(47m * 7, enhanced.Total);
        Assert.Equal(7, enhanced.CatastrophicMonth);

        // Without the credit, the copay alone never reaches the cap.
        Assert.Equal(47m * 12, equivalent.Total);
        Assert.Null(equivalent.CatastrophicMonth);
    }

    [Fact]
    public void Insulin_skips_the_deductible_and_is_capped_at_35_a_month()
    {
        var plan = Plan(615m, DrugBenefitType.DefinedStandard, Tier(3, Coins(0.25m), deductible: true));
        var result = Run(plan, Drug(price: 300m, tier: 3, insulin: true));

        Assert.Equal(35m * 12, result.Total);
    }

    [Fact]
    public void Insulin_uses_the_plans_lower_insulin_copay()
    {
        var insulin = new InsulinCost(3, 30, [20m, 20m, 20m, 20m], [null, null, null, null]);
        var plan = new PlanBenefit(Key, 615m, DrugBenefitType.DefinedStandard,
            Tier(3, Coins(0.25m), deductible: true), [insulin]);
        var result = Run(plan, Drug(price: 300m, tier: 3, insulin: true));

        Assert.Equal(20m * 12, result.Total);
    }

    [Fact]
    public void Extra_help_pays_down_to_the_generic_copay()
    {
        var plan = Plan(615m, DrugBenefitType.DefinedStandard, Tier(2, Coins(0.25m), deductible: true));
        var result = Run(plan, Drug(price: 12m, tier: 2), ExtraHelpLevel.Category2);

        Assert.Equal(1.60m * 12, result.Total);
    }

    [Fact]
    public void Extra_help_follows_the_unsubsidized_phases_and_pays_nothing_after_the_cap()
    {
        var plan = Plan(615m, DrugBenefitType.DefinedStandard, Tier(5, Coins(0.25m), deductible: true));
        var result = Run(plan, Drug(price: 1500m, tier: 5, kind: DrugKind.Brand), ExtraHelpLevel.Category1);

        // Same phases as without Extra Help: the cap is reached in May, so five fills at $12.65.
        Assert.Equal(12.65m * 5, result.Total);
        Assert.Equal(5, result.CatastrophicMonth);
    }

    [Fact]
    public void Drug_not_on_the_formulary_costs_full_price_and_does_not_count_toward_the_cap()
    {
        var plan = Plan(615m, DrugBenefitType.DefinedStandard, Tier(3, Coins(0.25m), deductible: true));
        var rx = new Prescription("1", "Drug", Quantity: 1m, DaysSupply: 30, DrugKind.Generic);
        var result = Run(plan, (rx, PlanDrug.NotCovered("1", Prices(100m))));

        Assert.Equal(1200m, result.Total);
        Assert.Equal(0m, result.TrueOutOfPocket);
        Assert.Equal(CoverageStatus.NotOnFormulary, result.Drugs[0].Status);
        Assert.True(result.Drugs[0].PriceEstimated);
    }

    [Fact]
    public void A_90_day_supply_the_tier_does_not_offer_costs_full_price()
    {
        // Specialty tier covered for 30-day fills only.
        var plan = Plan(0m, DrugBenefitType.DefinedStandard, Tier(5, Coins(0.25m), days: [30]));
        var result = Run(plan, Drug(price: 1200m, tier: 5, days: 90));

        Assert.Equal(CoverageStatus.DaysSupplyNotCovered, result.Drugs[0].Status);
        Assert.Equal(4, result.Drugs[0].Fills); // Jan, Apr, Jul, Oct
        Assert.Equal(4800m, result.Total);
    }

    [Fact]
    public void Deemed_rule_ends_the_plan_deductible_once_troop_reaches_the_standard_deductible()
    {
        // Enhanced plan: $300 deductible on tier 3 only; tier 2 has a $0 copay.
        // A $700 tier-2 fill credits $636.25 of standard-benefit share, past the $615 standard deductible.
        var plan = Plan(300m, DrugBenefitType.EnhancedAlternative,
            [.. Tier(2, Copay(0m)), .. Tier(3, Coins(0.25m), deductible: true)]);
        var drugs = new[] { Drug(price: 700m, tier: 2, rxcui: "A"), Drug(price: 100m, tier: 3, rxcui: "B") };

        var deemed = DrugCostSimulator.Simulate(plan, drugs, DispensingFees.None, PharmacyType.StandardRetail,
            Year2026 with { StartMonth = 12 });
        var notDeemed = DrugCostSimulator.Simulate(plan, drugs, DispensingFees.None, PharmacyType.StandardRetail,
            Year2026 with { StartMonth = 12, DeemedDeductibleRule = false });

        Assert.Equal(25m, deemed.Total);
        Assert.Equal(100m, notDeemed.Total);
    }

    [Fact]
    public void Tier_exempt_from_the_deductible_uses_its_pre_deductible_cost_sharing()
    {
        var zero = Copay(0m);
        var preDeductible = new TierCost(CoveragePhase.Deductible, 1, 30, [Copay(2m), Copay(2m), Copay(2m), Copay(2m)], false, false);
        var initial = new TierCost(CoveragePhase.Initial, 1, 30, [Copay(4m), Copay(4m), Copay(4m), Copay(4m)], false, false);
        var catastrophic = new TierCost(CoveragePhase.Catastrophic, 1, 30, [zero, zero, zero, zero], false, false);
        var plan = Plan(615m, DrugBenefitType.EnhancedAlternative, [preDeductible, initial, catastrophic]);

        var result = Run(plan, Drug(price: 20m, tier: 1));

        // TrOOP credit is the standard share ($20 a fill while under $615), so the deductible stays unmet all year.
        Assert.Equal(2m * 12, result.Total);
    }

    [Fact]
    public void Copay_never_exceeds_the_drug_price()
    {
        var plan = Plan(0m, DrugBenefitType.EnhancedAlternative, Tier(1, Copay(10m)));
        var result = Run(plan, Drug(price: 4m, tier: 1));

        Assert.Equal(48m, result.Total);
    }

    [Fact]
    public void Coinsurance_is_clamped_by_the_rows_maximum()
    {
        var plan = Plan(0m, DrugBenefitType.EnhancedAlternative, Tier(2, new CostShare(CostType.Coinsurance, 0.25m, Max: 5.10m)));
        var result = Run(plan, Drug(price: 100m, tier: 2));

        Assert.Equal(5.10m * 12, result.Total);
    }

    [Fact]
    public void Starting_mid_year_only_simulates_the_remaining_months()
    {
        var plan = Plan(0m, DrugBenefitType.EnhancedAlternative, Tier(1, Copay(5m)));
        var result = DrugCostSimulator.Simulate(plan, [Drug(price: 20m, tier: 1)], DispensingFees.None,
            PharmacyType.StandardRetail, Year2026 with { StartMonth = 10 });

        Assert.Equal(15m, result.Total);
        Assert.Equal(3, result.Drugs[0].Fills);
    }

    [Fact]
    public void Full_cost_is_unit_cost_times_quantity_plus_the_dispensing_fee()
    {
        var plan = Plan(0m, DrugBenefitType.EnhancedAlternative, Tier(3, Coins(0.25m)));
        var rx = new Prescription("1", "Drug", Quantity: 30m, DaysSupply: 30, DrugKind.Brand);
        var drug = new PlanDrug("1", CoverageStatus.Covered, Tier: 3,
            UnitCostByDaysSupply: new Dictionary<int, decimal> { [30] = 0.1018m });
        var fees = new DispensingFees([1.50m, 1.50m, 1.50m], [0.30m, 0.30m, 0.30m], [null, null, null]);

        var result = DrugCostSimulator.Simulate(plan, [(rx, drug)], fees, PharmacyType.StandardRetail, Year2026);

        Assert.Equal(4.55m, result.Drugs[0].FullCostPerFill); // 30 × $0.1018 = $3.054, + $1.50 brand fee
    }

    [Fact]
    public void Quantity_limit_is_flagged_only_when_the_prescription_is_over_it()
    {
        var plan = Plan(0m, DrugBenefitType.EnhancedAlternative, Tier(1, Copay(0m)));
        PlanDrug Limited(string rxcui) => new(rxcui, CoverageStatus.Covered, Tier: 1, QuantityLimit: true,
            QuantityLimitAmount: 30m, QuantityLimitDays: 30, UnitCostByDaysSupply: Prices(1m));
        Prescription Rx(string rxcui, decimal qty, int days) => new(rxcui, rxcui, qty, days, DrugKind.Generic);

        var result = DrugCostSimulator.Simulate(plan,
            [(Rx("within", 90m, 90), Limited("within")), (Rx("over", 60m, 30), Limited("over"))],
            DispensingFees.None, PharmacyType.StandardRetail, Year2026);

        Assert.False(result.Drugs[0].ExceedsQuantityLimit); // 90 per 90 days = 30 per 30
        Assert.True(result.Drugs[1].ExceedsQuantityLimit);
    }

    // --- helpers -------------------------------------------------------------------------------

    private static readonly PlanKey Key = new("S0000", "001", "000");

    private static DrugCostEstimate Run(PlanBenefit plan, (Prescription, PlanDrug) drug, ExtraHelpLevel extraHelp = ExtraHelpLevel.None) =>
        DrugCostSimulator.Simulate(plan, [drug], DispensingFees.None, PharmacyType.StandardRetail,
            Year2026 with { ExtraHelp = extraHelp });

    private static PlanBenefit Plan(decimal deductible, DrugBenefitType type, IEnumerable<TierCost> tiers) =>
        new(Key, deductible, type, tiers);

    /// <summary>Initial and $0 catastrophic rows, the same share at every pharmacy type.</summary>
    private static TierCost[] Tier(int tier, CostShare share, bool deductible = false, int[]? days = null)
    {
        var zero = Copay(0m);
        return (days ?? [30, 60, 90]).SelectMany(d => new[]
        {
            new TierCost(CoveragePhase.Initial, tier, d, [share, share, share, share], tier == 5, deductible),
            new TierCost(CoveragePhase.Catastrophic, tier, d, [zero, zero, zero, zero], tier == 5, deductible),
        }).ToArray();
    }

    private static CostShare Copay(decimal amount) => new(CostType.Copay, amount);
    private static CostShare Coins(decimal fraction) => new(CostType.Coinsurance, fraction);

    /// <summary>A covered drug whose full cost per fill is <paramref name="price"/> (quantity 1, no fees).</summary>
    private static (Prescription, PlanDrug) Drug(decimal price, int tier, bool insulin = false,
        DrugKind kind = DrugKind.Generic, string rxcui = "1", int days = 30) =>
        (new Prescription(rxcui, "Drug " + rxcui, Quantity: 1m, DaysSupply: days, kind, IsInsulin: insulin),
         new PlanDrug(rxcui, CoverageStatus.Covered, Tier: tier, UnitCostByDaysSupply: Prices(price)));

    private static IReadOnlyDictionary<int, decimal> Prices(decimal price) =>
        new Dictionary<int, decimal> { [30] = price, [60] = price, [90] = price };
}
