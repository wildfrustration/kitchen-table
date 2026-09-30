namespace PlanD.Engine;

public static class Money
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>One cell group of the beneficiary cost file: what the member pays at one pharmacy type.</summary>
public sealed record CostShare(CostType Type, decimal Amount, decimal Min = 0m, decimal Max = 0m)
{
    public static readonly CostShare NotOffered = new(CostType.NotOffered, 0m);

    public bool IsOffered => Type != CostType.NotOffered;

    /// <summary>
    /// The member's share of a fill with the given negotiated price, after min/max clamping
    /// and the lesser-of rule (never more than the drug's price).
    /// </summary>
    public decimal ShareOf(decimal price)
    {
        if (price <= 0m) return 0m;
        var share = Type switch
        {
            CostType.Copay => Amount,
            CostType.Coinsurance => Clamp(Amount * price),
            _ => throw new InvalidOperationException("Cost share is not offered at this pharmacy type."),
        };
        return Math.Min(Money.Round(share), price);
    }

    private decimal Clamp(decimal share)
    {
        if (Min > 0m) share = Math.Max(share, Min);
        if (Max > 0m) share = Math.Min(share, Max);
        return share;
    }
}

/// <summary>A row of the beneficiary cost file. Shares are indexed by <see cref="PharmacyType"/>.</summary>
public sealed record TierCost(
    CoveragePhase Phase,
    int Tier,
    int DaysSupply,
    IReadOnlyList<CostShare> Shares,
    bool Specialty,
    bool DeductibleApplies)
{
    public CostShare At(PharmacyType pharmacy) => Shares[(int)pharmacy];
}

/// <summary>A row of the insulin cost file. A null amount means not offered at that pharmacy type.</summary>
public sealed record InsulinCost(
    int? Tier,
    int DaysSupply,
    IReadOnlyList<decimal?> Copay,
    IReadOnlyList<decimal?> Coinsurance);

/// <summary>A plan's Part D cost-sharing structure.</summary>
public sealed class PlanBenefit
{
    private readonly Dictionary<(CoveragePhase, int, int), TierCost> _tiers;
    private readonly HashSet<int> _deductibleTiers;
    private readonly IReadOnlyList<InsulinCost> _insulin;

    public PlanBenefit(
        PlanKey key,
        decimal deductible,
        DrugBenefitType benefitType,
        IEnumerable<TierCost> tierCosts,
        IEnumerable<InsulinCost>? insulinCosts = null)
    {
        Key = key;
        Deductible = deductible;
        BenefitType = benefitType;
        _tiers = tierCosts.ToDictionary(t => (t.Phase, t.Tier, t.DaysSupply));
        _deductibleTiers = _tiers.Values.Where(t => t.DeductibleApplies).Select(t => t.Tier).ToHashSet();
        _insulin = insulinCosts?.ToList() ?? [];
    }

    public PlanKey Key { get; }
    public decimal Deductible { get; }
    public DrugBenefitType BenefitType { get; }

    public TierCost? Find(CoveragePhase phase, int tier, int daysSupply) =>
        _tiers.GetValueOrDefault((phase, tier, daysSupply));

    /// <summary>True when the plan deductible applies to this tier (DED_APPLIES_YN is constant per tier).</summary>
    public bool DeductibleApplies(int tier) => Deductible > 0m && _deductibleTiers.Contains(tier);

    public InsulinCost? FindInsulin(int tier, int daysSupply) =>
        _insulin.FirstOrDefault(i => i.Tier == tier && i.DaysSupply == daysSupply)
        ?? _insulin.FirstOrDefault(i => i.Tier is null && i.DaysSupply == daysSupply);

    /// <summary>Whether the plan offers initial-coverage cost sharing at this pharmacy type at all.</summary>
    public bool Offers(PharmacyType pharmacy) =>
        _tiers.Values.Any(t => t.Phase == CoveragePhase.Initial && t.At(pharmacy).IsOffered);
}

/// <summary>How one plan covers one drug, plus the plan's unit costs for it.</summary>
public sealed record PlanDrug(
    string Rxcui,
    CoverageStatus Status,
    int? Tier = null,
    bool PriorAuth = false,
    bool StepTherapy = false,
    bool QuantityLimit = false,
    decimal? QuantityLimitAmount = null,
    int? QuantityLimitDays = null,
    bool SelectedDrug = false,
    IReadOnlyDictionary<int, decimal>? UnitCostByDaysSupply = null,
    bool UnitCostIsFallback = false)
{
    public static PlanDrug NotCovered(string rxcui, IReadOnlyDictionary<int, decimal>? marketUnitCost) =>
        new(rxcui, CoverageStatus.NotOnFormulary, UnitCostByDaysSupply: marketUnitCost, UnitCostIsFallback: true);
}

/// <summary>What the person takes. Quantity is billing units per fill of <see cref="DaysSupply"/> days.</summary>
public sealed record Prescription(
    string Rxcui,
    string Name,
    decimal Quantity,
    int DaysSupply,
    DrugKind Kind,
    bool IsInsulin = false,
    bool IsVaccine = false)
{
    public int FillIntervalMonths => DaysSupply / 30;
}

/// <summary>Dispensing fees for one plan and pharmacy type, indexed 0/1/2 for 30/60/90 days.</summary>
public sealed record DispensingFees(
    IReadOnlyList<decimal> Brand,
    IReadOnlyList<decimal> Generic,
    IReadOnlyList<decimal?> Selected,
    decimal FloorPrice = 0m)
{
    public static readonly DispensingFees None = new([0m, 0m, 0m], [0m, 0m, 0m], [null, null, null]);

    public decimal For(DrugKind kind, bool selectedDrug, int daysSupply)
    {
        var i = daysSupply switch { 30 => 0, 60 => 1, 90 => 2, _ => throw new ArgumentOutOfRangeException(nameof(daysSupply)) };
        // Plan Finder uses the brand fee when a plan reports no selected-drug fee (SPUF methodology).
        if (selectedDrug) return Selected[i] ?? Brand[i];
        return kind == DrugKind.Brand ? Brand[i] : Generic[i];
    }
}
