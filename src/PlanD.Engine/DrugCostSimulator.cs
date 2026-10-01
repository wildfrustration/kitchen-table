namespace PlanD.Engine;

public sealed record SimulationOptions
{
    public required BenefitYearParameters Parameters { get; init; }

    /// <summary>First month of coverage, 1–12. Open-enrollment quotes cover the full year (1).</summary>
    public int StartMonth { get; init; } = 1;

    public ExtraHelpLevel ExtraHelp { get; init; } = ExtraHelpLevel.None;

    /// <summary>
    /// CY2025 Part D redesign (program instructions §40): the plan deductible counts as met once
    /// TrOOP reaches the standard deductible. Plan Finder's handling is unverified, so it's switchable.
    /// </summary>
    public bool DeemedDeductibleRule { get; init; } = true;
}

public sealed record DrugCostLine(
    string Rxcui,
    string Name,
    CoverageStatus Status,
    int? Tier,
    decimal FullCostPerFill,
    bool PriceEstimated,
    int Fills,
    decimal MemberCost,
    bool PriorAuth,
    bool StepTherapy,
    bool QuantityLimit,
    bool ExceedsQuantityLimit,
    bool PriceUnavailable)
{
    // PriceUnavailable: CMS publishes no unit cost for this drug (typically one no plan covers), so its cost
    // can't be estimated and isn't in the total.
}

public sealed record DrugCostEstimate(
    PharmacyType Pharmacy,
    decimal Total,
    IReadOnlyList<decimal> ByMonth,
    IReadOnlyList<DrugCostLine> Drugs,
    int? DeductibleMetMonth,
    int? CatastrophicMonth,
    decimal TrueOutOfPocket);

/// <summary>
/// Month-by-month Part D cost simulation for one plan at one pharmacy type.
/// Rules and sources: docs/research/part-d-cost-rules.md.
/// </summary>
public static class DrugCostSimulator
{
    public static DrugCostEstimate Simulate(
        PlanBenefit plan,
        IReadOnlyList<(Prescription Rx, PlanDrug Drug)> drugs,
        DispensingFees fees,
        PharmacyType pharmacy,
        SimulationOptions options)
    {
        if (options.StartMonth is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(options), "StartMonth must be 1–12.");

        var state = new State(plan.Deductible);
        var lines = drugs.Select(d => Line.Prepare(plan, d.Rx, d.Drug, fees, pharmacy)).ToArray();
        var byMonth = new decimal[12];

        for (var month = options.StartMonth; month <= 12; month++)
        {
            foreach (var line in lines)
            {
                if ((month - options.StartMonth) % line.Rx.FillIntervalMonths != 0) continue;

                var pay = line.CountsTowardBenefit
                    ? CoveredFill(plan, line, pharmacy, state, options)
                    : line.FullCost;

                line.Fills++;
                line.MemberCost += pay;
                byMonth[month - 1] += pay;
                state.MarkMonth(month);
            }
        }

        return new DrugCostEstimate(
            pharmacy,
            byMonth.Sum(),
            byMonth,
            lines.Select(l => l.ToResult()).ToList(),
            state.DeductibleMetMonth,
            state.CatastrophicMonth,
            state.Troop);
    }

    private static decimal CoveredFill(PlanBenefit plan, Line line, PharmacyType pharmacy, State state, SimulationOptions options)
    {
        var p = options.Parameters;
        var price = line.FullCost;
        var rx = line.Rx;
        var tier = line.Drug.Tier!.Value;

        // ACIP-recommended vaccines are $0 and don't touch TrOOP.
        if (rx.IsVaccine) return 0m;

        if (state.Catastrophic)
        {
            if (options.ExtraHelp != ExtraHelpLevel.None) return 0m;
            var cat = plan.Find(CoveragePhase.Catastrophic, tier, rx.DaysSupply)?.At(pharmacy);
            return cat is { IsOffered: true } ? cat.ShareOf(price) : 0m;
        }

        decimal planPay;
        if (rx.IsInsulin)
        {
            planPay = InsulinShare(plan, line, pharmacy, p);
        }
        else if (!state.DeductibleMet && plan.DeductibleApplies(tier))
        {
            var remaining = state.PlanDeductibleRemaining;
            if (price <= remaining)
            {
                planPay = price;
                state.PlanDeductibleRemaining -= price;
            }
            else
            {
                // The fill finishes the deductible: pay what's left of it, then initial-coverage
                // cost sharing on the rest. Copay handling for this straddle is unverified in Plan Finder.
                var initial = plan.Find(CoveragePhase.Initial, tier, rx.DaysSupply)!.At(pharmacy);
                planPay = remaining + initial.ShareOf(price - remaining);
                state.MeetDeductible();
            }
        }
        else
        {
            // Tiers the deductible skips have their own pre-deductible (level 0) cost sharing.
            var preDeductible = state.DeductibleMet
                ? null
                : plan.Find(CoveragePhase.Deductible, tier, rx.DaysSupply)?.At(pharmacy);
            var share = preDeductible is { IsOffered: true }
                ? preDeductible
                : plan.Find(CoveragePhase.Initial, tier, rx.DaysSupply)!.At(pharmacy);
            planPay = share.ShareOf(price);
        }

        // TrOOP. Since 2025, an enhanced plan's extra coverage counts too: each fill adds the larger
        // of the member's share and what the defined-standard benefit would have charged.
        var increment = plan.BenefitType == DrugBenefitType.EnhancedAlternative
            ? Math.Max(planPay, DefinedStandardShare(price, state.Troop, rx, p))
            : planPay;

        var toCap = p.OutOfPocketThreshold - state.Troop;
        if (increment >= toCap)
        {
            planPay = Math.Min(planPay, toCap);
            state.Troop = p.OutOfPocketThreshold;
            state.Catastrophic = true;
        }
        else
        {
            state.Troop += increment;
        }

        if (options.DeemedDeductibleRule && !state.DeductibleMet && state.Troop >= p.StandardDeductible)
            state.MeetDeductible();

        // Extra Help pays everything above its copay; the subsidy still counts toward TrOOP,
        // which is why the phases above follow the non-subsidized path.
        var extraHelpCopay = p.ExtraHelpCopay(options.ExtraHelp, rx.Kind);
        return extraHelpCopay is { } copay ? Math.Min(copay, planPay) : planPay;
    }

    private static decimal InsulinShare(PlanBenefit plan, Line line, PharmacyType pharmacy, BenefitYearParameters p)
    {
        var price = line.FullCost;
        var tier = line.Drug.Tier!.Value;
        var rx = line.Rx;
        var cap = p.InsulinMonthlyCap * rx.FillIntervalMonths;

        var row = plan.FindInsulin(tier, rx.DaysSupply);
        var copay = row?.Copay[(int)pharmacy];
        var coins = row?.Coinsurance[(int)pharmacy];

        decimal share;
        if (copay is null && coins is null)
        {
            // No insulin row: the tier's initial-coverage share, capped by law. The deductible never applies.
            share = plan.Find(CoveragePhase.Initial, tier, rx.DaysSupply)!.At(pharmacy).ShareOf(price);
        }
        else
        {
            share = price;
            if (copay is { } c) share = Math.Min(share, c);
            if (coins is { } k) share = Math.Min(share, Money.Round(k * price));
        }

        return Math.Min(Math.Min(share, cap), price);
    }

    /// <summary>What the defined-standard benefit would charge for this fill at the current TrOOP.</summary>
    private static decimal DefinedStandardShare(decimal price, decimal troop, Prescription rx, BenefitYearParameters p)
    {
        if (rx.IsVaccine) return 0m;
        if (rx.IsInsulin)
            return Math.Min(p.InsulinMonthlyCap * rx.FillIntervalMonths, Money.Round(p.StandardCoinsurance * price));

        var inDeductible = Math.Min(price, Math.Max(0m, p.StandardDeductible - troop));
        return Money.Round(inDeductible + p.StandardCoinsurance * (price - inDeductible));
    }

    private sealed class State(decimal planDeductible)
    {
        public decimal PlanDeductibleRemaining { get; set; } = planDeductible;
        public bool DeductibleMet { get; private set; } = planDeductible <= 0m;
        public bool Catastrophic { get; set; }
        public decimal Troop { get; set; }
        public int? DeductibleMetMonth { get; private set; }
        public int? CatastrophicMonth { get; private set; }

        private readonly bool _hasDeductible = planDeductible > 0m;

        public void MeetDeductible()
        {
            DeductibleMet = true;
            PlanDeductibleRemaining = 0m;
        }

        public void MarkMonth(int month)
        {
            if (_hasDeductible && DeductibleMet && DeductibleMetMonth is null) DeductibleMetMonth = month;
            if (Catastrophic && CatastrophicMonth is null) CatastrophicMonth = month;
        }
    }

    private sealed class Line
    {
        public required Prescription Rx { get; init; }
        public required PlanDrug Drug { get; init; }
        public required CoverageStatus Status { get; init; }
        public required decimal FullCost { get; init; }
        public required bool PriceEstimated { get; init; }
        public required bool PriceUnavailable { get; init; }
        public int Fills { get; set; }
        public decimal MemberCost { get; set; }

        public bool CountsTowardBenefit => Status is CoverageStatus.Covered or CoverageStatus.CoveredSupplemental;

        public static Line Prepare(PlanBenefit plan, Prescription rx, PlanDrug drug, DispensingFees fees, PharmacyType pharmacy)
        {
            if (rx.DaysSupply is not (30 or 60 or 90))
                throw new ArgumentException($"Days supply must be 30, 60 or 90 (got {rx.DaysSupply}).", nameof(rx));

            var status = drug.Status;
            if (status is CoverageStatus.Covered or CoverageStatus.CoveredSupplemental)
            {
                var row = drug.Tier is { } t ? plan.Find(CoveragePhase.Initial, t, rx.DaysSupply) : null;
                if (row is null || !row.At(pharmacy).IsOffered) status = CoverageStatus.DaysSupplyNotCovered;
            }

            var (unitCost, estimated) = UnitCost(drug, rx.DaysSupply);
            var fullCost = unitCost is { } u
                ? Money.Round(u * rx.Quantity + fees.For(rx.Kind, drug.SelectedDrug, rx.DaysSupply))
                : 0m;
            fullCost = Math.Max(fullCost, fees.FloorPrice);

            return new Line
            {
                Rx = rx,
                Drug = drug,
                Status = status,
                FullCost = fullCost,
                PriceEstimated = estimated || unitCost is null,
                PriceUnavailable = unitCost is null,
            };
        }

        private static (decimal? UnitCost, bool Estimated) UnitCost(PlanDrug drug, int daysSupply)
        {
            var costs = drug.UnitCostByDaysSupply;
            if (costs is null || costs.Count == 0) return (null, true);
            if (costs.TryGetValue(daysSupply, out var exact)) return (exact, drug.UnitCostIsFallback);
            // Unit costs are usually the same across supplies; borrow the closest one and flag it.
            var nearest = costs.OrderBy(c => Math.Abs(c.Key - daysSupply)).First().Value;
            return (nearest, true);
        }

        public DrugCostLine ToResult() => new(
            Rx.Rxcui, Rx.Name, Status, Drug.Tier, FullCost, PriceEstimated, Fills, MemberCost,
            Drug.PriorAuth, Drug.StepTherapy, Drug.QuantityLimit, ExceedsQuantityLimit(), PriceUnavailable);

        /// <summary>A quantity limit only matters when the prescription is over it (limits are per N days).</summary>
        private bool ExceedsQuantityLimit() =>
            Drug is { QuantityLimit: true, QuantityLimitAmount: { } amount, QuantityLimitDays: > 0 and var days }
            && Rx.Quantity > amount * Rx.DaysSupply / days;
    }
}
