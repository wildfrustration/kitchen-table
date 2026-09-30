namespace PlanD.Engine;

/// <summary>
/// Defined-standard Part D parameters for a plan year.
/// Sources: CY2026 and CY2027 Rate Announcements (Attachment V, Table V-2) — see docs/research/part-d-cost-rules.md.
/// </summary>
public sealed record BenefitYearParameters(
    int Year,
    decimal StandardDeductible,
    decimal OutOfPocketThreshold,
    decimal StandardCoinsurance,
    decimal InsulinMonthlyCap,
    ExtraHelpCopays Category1,
    ExtraHelpCopays Category2)
{
    public static readonly BenefitYearParameters Cy2026 = new(
        Year: 2026,
        StandardDeductible: 615m,
        OutOfPocketThreshold: 2100m,
        StandardCoinsurance: 0.25m,
        InsulinMonthlyCap: 35m,
        Category1: new ExtraHelpCopays(Generic: 5.10m, Other: 12.65m),
        Category2: new ExtraHelpCopays(Generic: 1.60m, Other: 4.90m));

    public static readonly BenefitYearParameters Cy2027 = new(
        Year: 2027,
        StandardDeductible: 700m,
        OutOfPocketThreshold: 2400m,
        StandardCoinsurance: 0.25m,
        InsulinMonthlyCap: 35m,
        Category1: new ExtraHelpCopays(Generic: 5.80m, Other: 14.40m),
        Category2: new ExtraHelpCopays(Generic: 1.65m, Other: 5.00m));

    public static BenefitYearParameters For(int year) => year switch
    {
        2026 => Cy2026,
        2027 => Cy2027,
        _ => throw new ArgumentOutOfRangeException(nameof(year), year, "No Part D parameters for this plan year."),
    };

    /// <summary>The Extra Help copay for a fill, or null when the person has no Extra Help.</summary>
    public decimal? ExtraHelpCopay(ExtraHelpLevel level, DrugKind kind) => level switch
    {
        ExtraHelpLevel.None => null,
        ExtraHelpLevel.Category1 => kind == DrugKind.Generic ? Category1.Generic : Category1.Other,
        ExtraHelpLevel.Category2 => kind == DrugKind.Generic ? Category2.Generic : Category2.Other,
        ExtraHelpLevel.Category3 => 0m,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };
}

public sealed record ExtraHelpCopays(decimal Generic, decimal Other);
