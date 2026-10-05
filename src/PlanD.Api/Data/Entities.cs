using Microsoft.AspNetCore.Identity;

namespace PlanD.Api.Data;

public sealed class Agency
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<BrokerUser> Brokers { get; set; } = [];
}

public enum BrokerRole
{
    Broker,
    AgencyAdmin,
}

/// <summary>A broker's login. Every broker belongs to one agency; a solo broker is an agency of one.</summary>
public sealed class BrokerUser : IdentityUser<Guid>
{
    public BrokerUser() => Id = Guid.CreateVersion7();

    public Guid AgencyId { get; set; }
    public Agency Agency { get; set; } = null!;
    public required string DisplayName { get; set; }
    public BrokerRole Role { get; set; }
    public string? PhotoUrl { get; set; }

    /// <summary>The broker's public intake link: /start/{PublicSlug}.</summary>
    public required string PublicSlug { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSignInAt { get; set; }

    /// <summary>Set by an agency admin: the broker can't sign in and their public link stops working.</summary>
    public DateTimeOffset? DeactivatedAt { get; set; }

    public List<BrokerCarrier> Carriers { get; set; } = [];
}

/// <summary>A PDP sponsor (landscape "Parent Organization Name") the broker is appointed with.</summary>
public sealed class BrokerCarrier
{
    public Guid BrokerId { get; set; }
    public required string ParentOrganization { get; set; }
}

public enum ClientStatus
{
    New,
    Reviewed,
    Contacted,
    Enrolled,
    Closed,
}

public enum ClientSource
{
    /// <summary>Arrived through the broker's public link.</summary>
    PublicLink,

    /// <summary>The broker invited this client.</summary>
    Invite,

    /// <summary>The broker entered the client by hand.</summary>
    Broker,
}

public enum MedicareStatus
{
    OnMedicare,
    TurningSixtyFive,
    NotSure,
}

/// <summary>What the patient says about help paying for drugs. The broker sets the engine level from it.</summary>
public enum ExtraHelpAnswer
{
    No,
    ExtraHelp,
    Medicaid,
    Ssi,
    MedicareSavingsProgram,
    NotSure,
}

public sealed class Client
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AgencyId { get; set; }
    public Guid BrokerId { get; set; }
    public BrokerUser Broker { get; set; } = null!;
    public ClientStatus Status { get; set; } = ClientStatus.New;
    public ClientSource Source { get; set; }

    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Zip { get; set; }

    /// <summary>SSA county code, chosen when the ZIP spans counties.</summary>
    public string? CountyCode { get; set; }

    public int? BirthMonth { get; set; }
    public int? BirthYear { get; set; }
    public MedicareStatus? MedicareStatus { get; set; }
    public string? CurrentPlan { get; set; }
    public ExtraHelpAnswer ExtraHelp { get; set; } = ExtraHelpAnswer.No;

    /// <summary>The Extra Help category the quote uses (0 none, 1–3); set by the broker.</summary>
    public int ExtraHelpLevel { get; set; }

    public bool UsesMailOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the patient last submitted or updated their intake; null until they do.</summary>
    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>When the broker last opened the client; the queue shows "new" while SubmittedAt is later.</summary>
    public DateTimeOffset? SeenByBrokerAt { get; set; }

    public List<ClientDrug> Drugs { get; set; } = [];
    public List<ClientPharmacy> Pharmacies { get; set; } = [];
    public List<Consent> Consents { get; set; } = [];
    public List<ClientNote> Notes { get; set; } = [];
}

/// <summary>A drug as entered: units per dose × doses a day, filled every DaysSupply days.</summary>
public sealed class ClientDrug
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClientId { get; set; }
    public required string Rxcui { get; set; }
    public required string Name { get; set; }
    public decimal UnitsPerDose { get; set; } = 1m;
    public decimal DosesPerDay { get; set; } = 1m;
    public int DaysSupply { get; set; } = 30;
    public int SortOrder { get; set; }

    public decimal QuantityPerFill => UnitsPerDose * DosesPerDay * DaysSupply;
}

public sealed class ClientPharmacy
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClientId { get; set; }
    public required string Npi { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? Zip { get; set; }
    public int SortOrder { get; set; }
}

public enum ConsentKind
{
    /// <summary>Permission for the broker to contact them.</summary>
    Contact,

    /// <summary>Permission to share their drug list and answers with the broker.</summary>
    ShareHealthInfo,
}

/// <summary>An append-only record of what the patient agreed to, when, and to which wording.</summary>
public sealed class Consent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClientId { get; set; }
    public ConsentKind Kind { get; set; }
    public required string TextVersion { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

public sealed class ClientNote
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClientId { get; set; }
    public Guid AuthorId { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum LinkPurpose
{
    /// <summary>A broker's invite to one client (first visit).</summary>
    Invite,

    /// <summary>An emailed link for a patient to come back and edit.</summary>
    Return,
}

/// <summary>Single-use link for a patient. Only the SHA-256 of the token is stored.</summary>
public sealed class PatientLink
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClientId { get; set; }
    public LinkPurpose Purpose { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

/// <summary>An agency admin's invitation for a new broker to set up their login. Only the SHA-256 of the token is stored.</summary>
public sealed class BrokerInvite
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AgencyId { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public BrokerRole Role { get; set; }
    public Guid InvitedById { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}

/// <summary>
/// What a client was shown: the quote as computed at print/present time, with the data releases behind it.
/// Never updated.
/// </summary>
public sealed class QuoteSnapshot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClientId { get; set; }
    public Guid BrokerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int PlanYear { get; set; }
    public int SpufReleaseId { get; set; }
    public int? LandscapeReleaseId { get; set; }
    public required string RequestJson { get; set; }
    public required string ResultJson { get; set; }
}
