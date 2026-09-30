namespace PlanD.Engine.Quoting;

public enum PharmacyPreference
{
    Retail,
    Mail,
}

/// <summary>A stand-alone Part D plan sold in the county's PDP region (SPUF + landscape).</summary>
public sealed record PlanOffer(
    PlanKey Key,
    string PlanName,
    string? Organization,
    DrugBenefitType? BenefitType,
    string FormularyId,
    decimal Deductible,
    decimal? Premium,
    decimal? LisPremium,
    string? StarRating,
    bool? LisBenchmark)
{
    /// <summary>Monthly premium, or what's left of it after Extra Help's premium subsidy.</summary>
    public decimal MonthlyPremium(ExtraHelpLevel extraHelp)
    {
        var premium = extraHelp == ExtraHelpLevel.None ? Premium : LisPremium ?? Premium;
        return Math.Max(0m, premium ?? 0m);
    }
}

/// <summary>A drug as the person takes it: quantity in billing units per fill of DaysSupply days.</summary>
public sealed record DrugRequest(string Rxcui, decimal Quantity, int DaysSupply);

/// <summary>What RxNorm says about a drug.</summary>
public sealed record DrugInfo(string Rxcui, string Name, string? TermType)
{
    private static readonly HashSet<string> BrandTypes = ["SBD", "BPCK", "SBDF", "SBDC", "SBDG", "BN"];

    /// <summary>Branded term types; everything else is priced and Extra Help-copaid as generic.</summary>
    public DrugKind Kind => TermType is not null && BrandTypes.Contains(TermType) ? DrugKind.Brand : DrugKind.Generic;

    public bool IsInsulin => Name.Contains("insulin", StringComparison.OrdinalIgnoreCase);

    // Approximation: ACIP-recommended vaccines are $0; the SPUF has no vaccine flag.
    public bool IsVaccine => Name.Contains("vaccine", StringComparison.OrdinalIgnoreCase);
}

public sealed record QuoteRequest
{
    public required int Year { get; init; }

    /// <summary>SSA state+county code (the code CMS files use).</summary>
    public required string CountyCode { get; init; }

    public required IReadOnlyList<DrugRequest> Drugs { get; init; }
    public PharmacyPreference Pharmacy { get; init; } = PharmacyPreference.Retail;
    public ExtraHelpLevel ExtraHelp { get; init; } = ExtraHelpLevel.None;
    public int StartMonth { get; init; } = 1;
    public bool DeemedDeductibleRule { get; init; } = true;
}

public sealed record PlanQuote(
    PlanOffer Plan,
    DrugCostEstimate Drugs,
    decimal MonthlyPremium,
    decimal AnnualPremium,
    decimal EstimatedAnnualCost,
    IReadOnlyList<string> Notes);

public sealed record Quote(QuoteRequest Request, IReadOnlyList<DrugInfo> Drugs, IReadOnlyList<PlanQuote> Plans);

/// <summary>Per-plan network facts for one pharmacy type.</summary>
public sealed record NetworkInfo(int PharmacyCount, int InAreaCount, DispensingFees Fees);

/// <summary>Everything the simulator needs for a set of plans and drugs.</summary>
public sealed record PlanDrugData(
    IReadOnlyDictionary<PlanKey, PlanBenefit> Benefits,
    IReadOnlyDictionary<(PlanKey Plan, string Rxcui), PlanDrug> Drugs,
    IReadOnlyDictionary<string, IReadOnlyDictionary<int, decimal>> MarketUnitCost,
    IReadOnlyDictionary<(PlanKey Plan, PharmacyType Type), NetworkInfo> Networks);

public interface IQuoteData
{
    /// <summary>The PDPs sold in the county's PDP region.</summary>
    Task<IReadOnlyList<PlanOffer>> PlansForCountyAsync(int year, string countyCode, CancellationToken ct = default);

    Task<IReadOnlyList<DrugInfo>> DrugInfoAsync(IReadOnlyCollection<string> rxcuis, CancellationToken ct = default);

    Task<PlanDrugData> PlanDrugDataAsync(int year, IReadOnlyList<PlanOffer> plans, IReadOnlyCollection<string> rxcuis, CancellationToken ct = default);
}
