namespace PlanD.Engine;

/// <summary>CMS plan identity. Segment is zero-padded to 3 digits ("000") everywhere in plan-d.</summary>
public readonly record struct PlanKey(string ContractId, string PlanId, string SegmentId)
{
    public override string ToString() => $"{ContractId}-{PlanId}-{SegmentId}";

    public static PlanKey Parse(string value)
    {
        var parts = value.Split('-', '_');
        if (parts.Length != 3)
            throw new FormatException($"Expected CONTRACT-PLAN-SEGMENT, got '{value}'.");
        return new PlanKey(parts[0], parts[1].PadLeft(3, '0'), parts[2].PadLeft(3, '0'));
    }
}

/// <summary>The four cost-sharing columns in the CMS beneficiary cost file.</summary>
public enum PharmacyType
{
    PreferredRetail = 0,
    StandardRetail = 1,
    PreferredMail = 2,
    StandardMail = 3,
}

public enum CostType
{
    NotOffered = 0,
    Copay = 1,
    Coinsurance = 2,
}

/// <summary>Values match the file's COVERAGE_LEVEL codes. There is no coverage gap since 2025.</summary>
public enum CoveragePhase
{
    Deductible = 0,
    Initial = 1,
    Catastrophic = 3,
}

/// <summary>From the landscape "Drug Benefit Type" column.</summary>
public enum DrugBenefitType
{
    DefinedStandard,
    ActuariallyEquivalent,
    BasicAlternative,
    EnhancedAlternative,
}

/// <summary>Extra Help (LIS) cost-sharing categories after the 2024 expansion.</summary>
public enum ExtraHelpLevel
{
    None,
    /// <summary>Full subsidy, other than full-dual at or below 100% FPL (2027: $5.80 / $14.40).</summary>
    Category1,
    /// <summary>Full-dual at or below 100% FPL (2027: $1.65 / $5.00).</summary>
    Category2,
    /// <summary>Institutionalized or home and community-based services: $0.</summary>
    Category3,
}

public enum DrugKind
{
    Generic,
    Brand,
}

/// <summary>Why a drug is or isn't covered by a plan.</summary>
public enum CoverageStatus
{
    Covered,
    /// <summary>A Part D-excluded drug the plan covers as a supplemental benefit.</summary>
    CoveredSupplemental,
    NotOnFormulary,
    /// <summary>On the formulary, but the plan doesn't cover this days supply at this pharmacy type.</summary>
    DaysSupplyNotCovered,
}

public static class DrugNames
{
    /// <summary>"… Extended Release Oral Tablet [Contrave]" → "Contrave"; names without a brand stay as they are.</summary>
    public static string Short(string name)
    {
        var open = name.LastIndexOf('[');
        return open >= 0 && name.EndsWith(']') ? name[(open + 1)..^1] : name;
    }
}
