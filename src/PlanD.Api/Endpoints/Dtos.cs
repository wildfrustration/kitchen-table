using PlanD.Api.Data;
using PlanD.Engine;

namespace PlanD.Api.Endpoints;

// --- shared ----------------------------------------------------------------------------------------

public sealed record DrugEntry(string Rxcui, string Name, decimal UnitsPerDose, decimal DosesPerDay, int DaysSupply);

public sealed record PharmacyEntry(string Npi, string Name, string? Address, string? Zip);

/// <summary>What a patient (or a broker on their behalf) enters.</summary>
public sealed record IntakeForm(
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string Zip,
    string? CountyCode,
    int? BirthMonth,
    int? BirthYear,
    MedicareStatus? MedicareStatus,
    string? CurrentPlan,
    ExtraHelpAnswer ExtraHelp,
    bool UsesMailOrder,
    IReadOnlyList<DrugEntry> Drugs,
    IReadOnlyList<PharmacyEntry> Pharmacies)
{
    public const int MaxDrugs = 30;
    public const int MaxPharmacies = 5;

    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string message) => errors[field] = [message];

        if (string.IsNullOrWhiteSpace(FirstName)) Add(nameof(FirstName), "First name is required.");
        if (string.IsNullOrWhiteSpace(LastName)) Add(nameof(LastName), "Last name is required.");
        if (string.IsNullOrWhiteSpace(Email) && string.IsNullOrWhiteSpace(Phone)) Add(nameof(Email), "Enter an email or a phone number.");
        if (Email is { Length: > 0 } e && !e.Contains('@')) Add(nameof(Email), "That email doesn't look right.");
        if (Zip is not { Length: 5 } || !Zip.All(char.IsAsciiDigit)) Add(nameof(Zip), "ZIP must be 5 digits.");
        if (BirthMonth is < 1 or > 12) Add(nameof(BirthMonth), "Month must be 1–12.");
        if (BirthYear is { } y && (y < 1900 || y > DateTime.UtcNow.Year)) Add(nameof(BirthYear), "Check the birth year.");
        if (Drugs.Count > MaxDrugs) Add(nameof(Drugs), $"Up to {MaxDrugs} drugs.");
        if (Pharmacies.Count > MaxPharmacies) Add(nameof(Pharmacies), $"Up to {MaxPharmacies} pharmacies.");
        foreach (var d in Drugs)
        {
            if (d.UnitsPerDose <= 0 || d.DosesPerDay <= 0) Add(nameof(Drugs), $"{d.Name}: dose and frequency must be positive.");
            if (d.DaysSupply is not (30 or 60 or 90)) Add(nameof(Drugs), $"{d.Name}: fills must be 30, 60 or 90 days.");
        }
        return errors;
    }
}

// --- patient side ----------------------------------------------------------------------------------

public sealed record PublicBroker(string Slug, string DisplayName, string AgencyName, string? Phone, string? PhotoUrl);

/// <summary>The CMS third-party marketing disclaimer with the broker's counts for the patient's area.</summary>
public sealed record Disclaimer(int Organizations, int Plans, string Text);

public sealed record ConsentTexts(string Version, string Contact, string ShareHealthInfo);

public sealed record PublicIntake(IntakeForm Form, bool ConsentContact, bool ConsentShareHealthInfo);

public sealed record RedeemRequest(string Token);

public sealed record LinkRequest(string Email);

public sealed record PatientIntakeView(PublicBroker Broker, IntakeForm Form, DateTimeOffset? SubmittedAt);

// --- broker side -----------------------------------------------------------------------------------

public sealed record ClientSummary(
    Guid Id,
    string Name,
    string? Zip,
    ClientStatus Status,
    ClientSource Source,
    bool IsNew,
    DateTimeOffset? SubmittedAt,
    int DrugCount,
    Guid BrokerId,
    string BrokerName);

public sealed record NoteView(Guid Id, string Body, string Author, DateTimeOffset CreatedAt);

public sealed record ConsentView(ConsentKind Kind, string TextVersion, DateTimeOffset GrantedAt);

public sealed record ClientDetail(
    Guid Id,
    ClientStatus Status,
    ClientSource Source,
    IntakeForm Form,
    int ExtraHelpLevel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    Guid BrokerId,
    string BrokerName,
    IReadOnlyList<NoteView> Notes,
    IReadOnlyList<ConsentView> Consents);

public sealed record BrokerClientForm(IntakeForm Form, int ExtraHelpLevel);

public sealed record StatusChange(ClientStatus Status);

public sealed record NewNote(string Body);

public sealed record InviteLink(string Url, DateTimeOffset ExpiresAt, bool Emailed);

public sealed record QuoteOptions(int? PlanYear, int? StartMonth);

public sealed record CarrierChoice(string ParentOrganization, int Plans, bool Appointed);

public sealed record CarrierSelection(IReadOnlyList<string> ParentOrganizations);

// --- quotes ----------------------------------------------------------------------------------------

public sealed record QuoteDrugLine(
    string Rxcui, string Name, CoverageStatus Status, int? Tier, decimal FullCostPerFill, bool PriceEstimated,
    int Fills, decimal MemberCost, bool PriorAuth, bool StepTherapy, bool QuantityLimit, bool ExceedsQuantityLimit);

public sealed record QuotePharmacyStatus(string Npi, string Name, bool InNetwork, bool Preferred);

public sealed record QuotePlan(
    string Key,
    string Name,
    string? Organization,
    string? ParentOrganization,
    bool Appointed,
    string? BenefitType,
    string? StarRating,
    decimal Deductible,
    decimal MonthlyPremium,
    decimal AnnualPremium,
    decimal DrugCost,
    decimal EstimatedAnnualCost,
    bool AllDrugsCovered,
    bool? PharmacyInNetwork,
    PharmacyType PricedAt,
    string? PricedAtNpi,
    int? DeductibleMetMonth,
    int? CatastrophicMonth,
    IReadOnlyList<decimal> ByMonth,
    IReadOnlyList<QuoteDrugLine> Drugs,
    IReadOnlyList<QuotePharmacyStatus> Pharmacies,
    IReadOnlyList<string> Notes);

public sealed record QuoteResult(
    int PlanYear,
    int StartMonth,
    string CountyCode,
    string CountyName,
    string DataRelease,
    DateTimeOffset CalculatedAt,
    IReadOnlyList<QuotePlan> Plans,
    IReadOnlyList<string> Warnings);

public sealed record SnapshotCreated(Guid Id);

public sealed record SnapshotView(
    Guid Id,
    DateTimeOffset CreatedAt,
    string ClientName,
    string BrokerName,
    string AgencyName,
    string? BrokerPhone,
    Disclaimer Disclaimer,
    QuoteResult Quote);
