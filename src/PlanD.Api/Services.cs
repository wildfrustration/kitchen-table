using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlanD.Api.Data;
using PlanD.Api.Endpoints;
using PlanD.Data;
using PlanD.Engine;
using PlanD.Engine.Quoting;

namespace PlanD.Api;

public static class ClientMapping
{
    public static IntakeForm ToForm(this Client c) => new(
        c.FirstName, c.LastName, c.Email, c.Phone, c.Zip ?? "", c.CountyCode, c.BirthMonth, c.BirthYear, c.MedicareStatus,
        c.CurrentPlan, c.ExtraHelp, c.UsesMailOrder,
        c.Drugs.OrderBy(d => d.SortOrder).Select(d => new DrugEntry(d.Rxcui, d.Name, d.UnitsPerDose, d.DosesPerDay, d.DaysSupply)).ToList(),
        c.Pharmacies.OrderBy(p => p.SortOrder).Select(p => new PharmacyEntry(p.Npi, p.Name, p.Address, p.Zip)).ToList());

    /// <summary>Copies the form onto the client, replacing its drug and pharmacy lists.</summary>
    public static void Apply(this Client c, IntakeForm f, AppDbContext db)
    {
        c.FirstName = f.FirstName.Trim();
        c.LastName = f.LastName.Trim();
        c.Email = string.IsNullOrWhiteSpace(f.Email) ? null : f.Email.Trim().ToLowerInvariant();
        c.Phone = string.IsNullOrWhiteSpace(f.Phone) ? null : f.Phone.Trim();
        if (c.Zip != f.Zip) c.CountyCode = null;
        c.Zip = f.Zip;
        c.CountyCode = f.CountyCode ?? c.CountyCode;
        c.BirthMonth = f.BirthMonth;
        c.BirthYear = f.BirthYear;
        c.MedicareStatus = f.MedicareStatus;
        c.CurrentPlan = string.IsNullOrWhiteSpace(f.CurrentPlan) ? null : f.CurrentPlan.Trim();
        c.ExtraHelp = f.ExtraHelp;
        c.UsesMailOrder = f.UsesMailOrder;
        c.UpdatedAt = DateTimeOffset.UtcNow;

        // Replace the lists. The new rows must be added explicitly: their ids are set in code, so EF would
        // otherwise take them for existing rows found through the navigation and try to UPDATE them.
        db.ClientDrugs.RemoveRange(c.Drugs);
        c.Drugs = f.Drugs.Select((d, i) => new ClientDrug
        {
            ClientId = c.Id, Rxcui = d.Rxcui, Name = d.Name, UnitsPerDose = d.UnitsPerDose,
            DosesPerDay = d.DosesPerDay, DaysSupply = d.DaysSupply, SortOrder = i,
        }).ToList();
        db.ClientDrugs.AddRange(c.Drugs);

        db.ClientPharmacies.RemoveRange(c.Pharmacies);
        c.Pharmacies = f.Pharmacies.Select((p, i) => new ClientPharmacy
        {
            ClientId = c.Id, Npi = p.Npi, Name = p.Name, Address = p.Address, Zip = p.Zip, SortOrder = i,
        }).ToList();
        db.ClientPharmacies.AddRange(c.Pharmacies);
    }

    /// <summary>A starting Extra Help category from the patient's answer; the broker confirms it.</summary>
    public static int SuggestedExtraHelpLevel(ExtraHelpAnswer answer) => answer switch
    {
        ExtraHelpAnswer.Medicaid or ExtraHelpAnswer.Ssi => 2,
        ExtraHelpAnswer.ExtraHelp or ExtraHelpAnswer.MedicareSavingsProgram => 1,
        _ => 0,
    };
}

public static class ConsentWording
{
    public const string Version = "2026-10-01";

    public static ConsentTexts For(string brokerName, string agencyName) => new(
        Version,
        $"I agree that {brokerName} of {agencyName} may contact me by phone, email or text about Medicare prescription drug plans.",
        $"I agree to share the prescriptions, pharmacies and answers I enter here with {brokerName} so they can compare plans for me.");
}

/// <summary>Quotes a client: resolves their county, runs the engine, and shapes the result for the broker workspace.</summary>
public sealed class ClientQuotes(QuoteService quotes, QuoteRepository repo, NpgsqlDataSource cms)
{
    public async Task<QuoteResult> QuoteAsync(Client client, IReadOnlySet<string> appointed, QuoteOptions options, CancellationToken ct)
    {
        var warnings = new List<string>();
        var year = options.PlanYear ?? await repo.LatestPlanYearAsync(ct);
        var counties = client.Zip is null ? [] : await repo.ResolveZipAsync(client.Zip, ct);
        var county = counties.FirstOrDefault(c => c.CountyCode == client.CountyCode) ?? counties.FirstOrDefault()
                     ?? throw new InvalidOperationException($"ZIP {client.Zip} isn't in the crosswalk.");
        if (counties.Count > 1 && client.CountyCode is null)
            warnings.Add($"ZIP {client.Zip} spans {counties.Count} counties; quoting {county.CountyName}. Pick the county on the client page if that's wrong.");

        var startMonth = options.StartMonth ?? (year > DateTime.UtcNow.Year ? 1 : Math.Min(12, DateTime.UtcNow.Month + 1));
        var request = new QuoteRequest
        {
            Year = year,
            CountyCode = county.CountyCode,
            Drugs = client.Drugs.OrderBy(d => d.SortOrder).Select(d => new DrugRequest(d.Rxcui, d.QuantityPerFill, d.DaysSupply)).ToList(),
            Pharmacy = client.UsesMailOrder ? PharmacyPreference.Mail : PharmacyPreference.Retail,
            ExtraHelp = (ExtraHelpLevel)Math.Clamp(client.ExtraHelpLevel, 0, 3),
            StartMonth = startMonth,
            PharmacyNpis = client.Pharmacies.OrderBy(p => p.SortOrder).Select(p => p.Npi).ToList(),
        };
        if (request.Drugs.Count == 0) warnings.Add("No drugs entered: totals are premiums only.");

        var quote = await quotes.QuoteAsync(request, ct);

        // A drug no plan covers usually has no CMS price at all: say so once, not just per plan.
        var unpriced = quote.Plans.SelectMany(p => p.Drugs.Drugs)
            .GroupBy(d => d.Rxcui)
            .Where(g => g.All(d => d.PriceUnavailable && d.Status == CoverageStatus.NotOnFormulary))
            .Select(g => DrugNames.Short(g.First().Name))
            .ToList();
        foreach (var name in unpriced)
            warnings.Add($"No plan here covers {name}, and CMS publishes no price for it, so the totals leave it out. The client would pay the pharmacy's cash price.");
        var names = client.Pharmacies.ToDictionary(p => p.Npi, p => p.Name);
        var release = await ReleaseLabelAsync(year, ct);

        return new QuoteResult(year, startMonth, county.CountyCode, county.CountyName, release, DateTimeOffset.UtcNow,
            quote.Plans.Select(q => ToPlan(q, appointed, names)).ToList(), warnings);
    }

    private static QuotePlan ToPlan(PlanQuote q, IReadOnlySet<string> appointed, IReadOnlyDictionary<string, string> pharmacyNames) => new(
        q.Plan.Key.ToString(), q.Plan.PlanName, q.Plan.Organization, q.Plan.ParentOrganization,
        q.Plan.ParentOrganization is { } parent && appointed.Contains(parent),
        q.Plan.BenefitType?.ToString(), q.Plan.StarRating, q.Plan.Deductible,
        q.MonthlyPremium, q.AnnualPremium, q.Drugs.Total, q.EstimatedAnnualCost, q.AllDrugsCovered, q.PharmacyInNetwork,
        q.Drugs.Pharmacy, q.PricedAtNpi, q.Drugs.DeductibleMetMonth, q.Drugs.CatastrophicMonth, q.Drugs.ByMonth,
        q.Drugs.Drugs.Select(d => new QuoteDrugLine(d.Rxcui, d.Name, d.Status, d.Tier, d.FullCostPerFill, d.PriceEstimated, d.Fills,
            d.MemberCost, d.PriorAuth, d.StepTherapy, d.QuantityLimit, d.ExceedsQuantityLimit, d.PriceUnavailable)).ToList(),
        q.Pharmacies.Select(p => new QuotePharmacyStatus(p.Npi, pharmacyNames.GetValueOrDefault(p.Npi, p.Npi), p.InNetwork, p.Preferred)).ToList(),
        q.Notes);

    private async Task<string> ReleaseLabelAsync(int year, CancellationToken ct)
    {
        await using var conn = await cms.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<string>(
            "select label from cms.release where source = 'spuf' and plan_year = @year and status = 'active'", new { year }) ?? "unknown";
    }

    /// <summary>
    /// The CMS third-party marketing disclaimer: how many PDP sponsors the broker represents in the patient's area
    /// and how many plans they offer there.
    /// </summary>
    public async Task<Disclaimer> DisclaimerAsync(IReadOnlySet<string> appointed, string? zip, CancellationToken ct)
    {
        int organizations = 0, plans = 0;
        if (zip is { Length: 5 } && (await repo.ResolveZipAsync(zip, ct)).FirstOrDefault() is { } county)
        {
            var offers = await repo.PlansForCountyAsync(await repo.LatestPlanYearAsync(ct), county.CountyCode, ct);
            var represented = offers.Where(o => o.ParentOrganization is { } p && appointed.Contains(p)).ToList();
            organizations = represented.Select(o => o.ParentOrganization).Distinct().Count();
            plans = represented.Count;
        }
        var text = "We do not offer every plan available in your area. " +
                   (zip is null ? "" : $"Currently we represent {organizations} organizations which offer {plans} products in your area. ") +
                   "Please contact Medicare.gov, 1-800-MEDICARE, or your local State Health Insurance Program (SHIP) to get information on all of your options.";
        return new Disclaimer(organizations, plans, text);
    }
}

/// <summary>Emails. Broker alerts carry no health information — only a link into the workspace.</summary>
public sealed class Notifications(IEmailSender email, EmailOptions options, ILogger<Notifications> log)
{
    public async Task PatientReturnLinkAsync(Client client, BrokerUser broker, string token, CancellationToken ct)
    {
        if (client.Email is null) return;
        await Send(new EmailMessage(client.Email, $"Update your prescription list for {broker.DisplayName}",
            $"""
            Hi {client.FirstName},

            You can review or update the prescriptions and pharmacies you shared with {broker.DisplayName} here:

            {options.BaseUrl}/r/{token}

            The link works once, for 24 hours. If it expires, ask for a new one on the same page.
            """, broker.Email), ct);
    }

    public async Task InviteAsync(Client client, BrokerUser broker, string token, CancellationToken ct)
    {
        if (client.Email is null) return;
        await Send(new EmailMessage(client.Email, $"{broker.DisplayName} invited you to share your prescriptions",
            $"""
            Hi {client.FirstName},

            {broker.DisplayName} asked you to list your prescriptions and pharmacies so they can compare Medicare drug plans for you:

            {options.BaseUrl}/i/{token}

            The link works once, for 24 hours.
            """, broker.Email), ct);
    }

    public async Task BrokerNewIntakeAsync(Client client, BrokerUser broker, bool updated, CancellationToken ct)
    {
        if (broker.Email is null) return;
        await Send(new EmailMessage(broker.Email, updated ? "A client updated their intake" : "New client intake",
            $"""
            {(updated ? "A client updated their information." : "A new client shared their prescriptions with you.")}

            Open it in Kitchen Table: {options.BaseUrl}/app/clients/{client.Id}
            """), ct);
    }

    public async Task BrokerInviteAsync(BrokerInvite invite, BrokerUser invitedBy, string token, CancellationToken ct)
    {
        var role = invite.Role == BrokerRole.AgencyAdmin ? "an admin" : "a broker";
        await Send(new EmailMessage(invite.Email, $"Join {invitedBy.Agency.Name} on Kitchen Table",
            $"""
            Hi {invite.DisplayName},

            {invitedBy.DisplayName} added you as {role} at {invitedBy.Agency.Name} on Kitchen Table, where you'll compare Medicare drug plans for your clients.

            Set up your login here:

            {options.BaseUrl}/join/{token}

            The link works once, for {BrokerInvite.Lifetime.Days} days.
            """, invitedBy.Email), ct);
    }

    private async Task Send(EmailMessage message, CancellationToken ct)
    {
        try
        {
            await email.SendAsync(message, ct);
        }
        catch (Exception e)
        {
            // A failed email must not lose the patient's submission.
            log.LogError(e, "Email to {To} failed", message.To);
        }
    }
}

public static class BrokerScope
{
    /// <summary>Clients a broker may see: their own, or the whole agency's for an agency admin.</summary>
    public static IQueryable<Client> VisibleTo(this IQueryable<Client> clients, BrokerUser broker) =>
        broker.Role == BrokerRole.AgencyAdmin
            ? clients.Where(c => c.AgencyId == broker.AgencyId)
            : clients.Where(c => c.BrokerId == broker.Id);

    public static async Task<HashSet<string>> CarriersAsync(this AppDbContext db, Guid brokerId, CancellationToken ct) =>
        (await db.BrokerCarriers.Where(c => c.BrokerId == brokerId).Select(c => c.ParentOrganization).ToListAsync(ct)).ToHashSet();
}
