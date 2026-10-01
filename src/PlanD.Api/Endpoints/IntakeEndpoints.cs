using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using PlanD.Api.Data;

namespace PlanD.Api.Endpoints;

/// <summary>
/// Patient-facing endpoints. A patient arrives through a broker's public link (/start/{slug}) or a single-use
/// invite/return link, sees only a confirmation after submitting, and never sees plans.
/// </summary>
public static class IntakeEndpoints
{
    public static void MapIntakeEndpoints(this RouteGroupBuilder api)
    {
        var intake = api.MapGroup("/intake").WithTags("Intake");

        intake.MapGet("/{slug}", async Task<Results<Ok<PublicBroker>, NotFound>> (string slug, AppDbContext db, CancellationToken ct) =>
            await PublicBrokerAsync(db, slug, ct) is { } b ? TypedResults.Ok(b) : TypedResults.NotFound());

        intake.MapGet("/{slug}/consents", async Task<Results<Ok<ConsentTexts>, NotFound>> (string slug, AppDbContext db, CancellationToken ct) =>
            await PublicBrokerAsync(db, slug, ct) is { } b
                ? TypedResults.Ok(ConsentWording.For(b.DisplayName, b.AgencyName))
                : TypedResults.NotFound());

        intake.MapGet("/{slug}/disclaimer", async Task<Results<Ok<Disclaimer>, NotFound>> (
            string slug, string? zip, AppDbContext db, ClientQuotes quotes, CancellationToken ct) =>
        {
            var broker = await db.Users.SingleOrDefaultAsync(u => u.PublicSlug == slug, ct);
            if (broker is null) return TypedResults.NotFound();
            return TypedResults.Ok(await quotes.DisclaimerAsync(await db.CarriersAsync(broker.Id, ct), zip, ct));
        });

        // The public link: anyone can submit, and the intake lands in that broker's queue.
        intake.MapPost("/{slug}", async Task<Results<Created, NotFound, ValidationProblem>> (
            string slug, PublicIntake body, HttpContext http, AppDbContext db, Notifications notify, CancellationToken ct) =>
        {
            var broker = await db.Users.SingleOrDefaultAsync(u => u.PublicSlug == slug, ct);
            if (broker is null) return TypedResults.NotFound();
            if (Validate(body) is { Count: > 0 } errors) return TypedResults.ValidationProblem(errors);

            var client = new Client
            {
                AgencyId = broker.AgencyId, BrokerId = broker.Id, Source = ClientSource.PublicLink,
                FirstName = body.Form.FirstName, LastName = body.Form.LastName,
            };
            client.Apply(body.Form, db);
            client.ExtraHelpLevel = ClientMapping.SuggestedExtraHelpLevel(body.Form.ExtraHelp);
            client.SubmittedAt = DateTimeOffset.UtcNow;
            AddConsents(db, client, http);
            db.Clients.Add(client);
            var token = NewLink(db, client, LinkPurpose.Return);
            await db.SaveChangesAsync(ct);

            await SignInPatient(http, client.Id);
            await notify.PatientReturnLinkAsync(client, broker, token, ct);
            await notify.BrokerNewIntakeAsync(client, broker, updated: false, ct);
            return TypedResults.Created();
        });

        // Emails a fresh return link when the address matches one of this broker's clients. Always 204 so the
        // endpoint can't be used to find out who is a client.
        intake.MapPost("/{slug}/link", async (string slug, LinkRequest body, AppDbContext db, Notifications notify, CancellationToken ct) =>
        {
            var email = body.Email.Trim().ToLowerInvariant();
            var client = await db.Clients.Include(c => c.Broker)
                .Where(c => c.Broker.PublicSlug == slug && c.Email == email)
                .OrderByDescending(c => c.UpdatedAt).FirstOrDefaultAsync(ct);
            if (client is not null)
            {
                var token = NewLink(db, client, LinkPurpose.Return);
                await db.SaveChangesAsync(ct);
                await notify.PatientReturnLinkAsync(client, client.Broker, token, ct);
            }
            return TypedResults.NoContent();
        });

        var patient = api.MapGroup("/patient").WithTags("Patient");

        // Invite and return links: single use, 24 hours, then a 7-day session scoped to that one client.
        patient.MapPost("/redeem", async Task<Results<Ok<PublicBroker>, UnauthorizedHttpResult>> (
            RedeemRequest body, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var hash = Tokens.Hash(body.Token);
            var link = await db.PatientLinks.SingleOrDefaultAsync(l => l.TokenHash == hash, ct);
            if (link is null || link.UsedAt is not null || link.ExpiresAt < DateTimeOffset.UtcNow) return TypedResults.Unauthorized();

            link.UsedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await SignInPatient(http, link.ClientId);
            var client = await db.Clients.Include(c => c.Broker).ThenInclude(b => b.Agency).SingleAsync(c => c.Id == link.ClientId, ct);
            return TypedResults.Ok(ToPublic(client.Broker));
        });

        patient.MapGet("/intake", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var client = await LoadClient(db, PatientAuth.ClientId(http.User), ct);
            return TypedResults.Ok(new PatientIntakeView(ToPublic(client.Broker), client.ToForm(), client.SubmittedAt));
        }).RequireAuthorization(Policies.Patient);

        patient.MapGet("/consents", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var client = await LoadClient(db, PatientAuth.ClientId(http.User), ct);
            return TypedResults.Ok(ConsentWording.For(client.Broker.DisplayName, client.Broker.Agency.Name));
        }).RequireAuthorization(Policies.Patient);

        patient.MapPut("/intake", async Task<Results<NoContent, ValidationProblem>> (
            PublicIntake body, HttpContext http, AppDbContext db, Notifications notify, CancellationToken ct) =>
        {
            if (Validate(body) is { Count: > 0 } errors) return TypedResults.ValidationProblem(errors);
            var client = await LoadClient(db, PatientAuth.ClientId(http.User), ct);
            var first = client.SubmittedAt is null;
            client.Apply(body.Form, db);
            if (client.ExtraHelpLevel == 0) client.ExtraHelpLevel = ClientMapping.SuggestedExtraHelpLevel(body.Form.ExtraHelp);
            client.SubmittedAt = DateTimeOffset.UtcNow;
            AddConsents(db, client, http);
            await db.SaveChangesAsync(ct);
            await notify.BrokerNewIntakeAsync(client, client.Broker, updated: !first, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization(Policies.Patient);

        patient.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(PatientAuth.Scheme);
            return TypedResults.NoContent();
        });
    }

    /// <summary>Creates a single-use patient link and returns the raw token (only its hash is stored).</summary>
    public static string NewLink(AppDbContext db, Client client, LinkPurpose purpose)
    {
        var token = Tokens.New();
        db.PatientLinks.Add(new PatientLink
        {
            ClientId = client.Id, Purpose = purpose, TokenHash = Tokens.Hash(token),
            ExpiresAt = DateTimeOffset.UtcNow + PatientAuth.LinkLifetime,
        });
        return token;
    }

    public static PublicBroker ToPublic(BrokerUser b) => new(b.PublicSlug, b.DisplayName, b.Agency.Name, b.PhoneNumber, b.PhotoUrl);

    private static async Task<PublicBroker?> PublicBrokerAsync(AppDbContext db, string slug, CancellationToken ct) =>
        await db.Users.Where(u => u.PublicSlug == slug)
            .Select(u => new PublicBroker(u.PublicSlug, u.DisplayName, u.Agency.Name, u.PhoneNumber, u.PhotoUrl))
            .SingleOrDefaultAsync(ct);

    private static Task<Client> LoadClient(AppDbContext db, Guid id, CancellationToken ct) =>
        db.Clients.Include(c => c.Drugs).Include(c => c.Pharmacies).Include(c => c.Broker).ThenInclude(b => b.Agency)
            .SingleAsync(c => c.Id == id, ct);

    private static Dictionary<string, string[]> Validate(PublicIntake body)
    {
        var errors = body.Form.Validate();
        if (!body.ConsentContact) errors["ConsentContact"] = ["Please agree to be contacted so your broker can reach you."];
        if (!body.ConsentShareHealthInfo) errors["ConsentShareHealthInfo"] = ["Please agree to share your prescriptions with your broker."];
        return errors;
    }

    private static void AddConsents(AppDbContext db, Client client, HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString();
        var agent = http.Request.Headers.UserAgent.ToString();
        foreach (var kind in new[] { ConsentKind.Contact, ConsentKind.ShareHealthInfo })
            db.Consents.Add(new Consent { ClientId = client.Id, Kind = kind, TextVersion = ConsentWording.Version, IpAddress = ip, UserAgent = agent });
    }

    private static Task SignInPatient(HttpContext http, Guid clientId) =>
        http.SignInAsync(PatientAuth.Scheme,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(PatientAuth.ClientIdClaim, clientId.ToString())], PatientAuth.Scheme)),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow + PatientAuth.SessionLength });
}
