using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using PlanD.Api.Data;
using PlanD.Data;

namespace PlanD.Api.Endpoints;

/// <summary>The broker workspace: queue, client page, quote, snapshots, carrier checklist.</summary>
public static class ClientEndpoints
{
    public static void MapClientEndpoints(this RouteGroupBuilder api)
    {
        var clients = api.MapGroup("/clients").WithTags("Clients").RequireAuthorization(Policies.Broker);

        clients.MapGet("/", async (ClientStatus? status, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var broker = await CurrentBroker(http, db, ct);
            var query = db.Clients.VisibleTo(broker);
            if (status is { } s) query = query.Where(c => c.Status == s);
            var list = await query
                .OrderByDescending(c => c.SubmittedAt ?? c.CreatedAt)
                .Select(c => new ClientSummary(c.Id, c.FirstName + " " + c.LastName, c.Zip, c.Status, c.Source,
                    c.SubmittedAt != null && (c.SeenByBrokerAt == null || c.SubmittedAt > c.SeenByBrokerAt),
                    c.SubmittedAt, c.Drugs.Count, c.BrokerId, c.Broker.DisplayName))
                .ToListAsync(ct);
            return TypedResults.Ok(list);
        });

        clients.MapPost("/", async Task<Results<Created<ClientDetail>, ValidationProblem>> (
            BrokerClientForm body, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            if (body.Form.Validate() is { Count: > 0 } errors) return TypedResults.ValidationProblem(errors);
            var broker = await CurrentBroker(http, db, ct);
            var client = new Client
            {
                AgencyId = broker.AgencyId, BrokerId = broker.Id, Source = ClientSource.Broker,
                FirstName = body.Form.FirstName, LastName = body.Form.LastName, ExtraHelpLevel = body.ExtraHelpLevel,
                SeenByBrokerAt = DateTimeOffset.UtcNow,
            };
            client.Apply(body.Form, db);
            db.Clients.Add(client);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created($"/api/clients/{client.Id}", await Detail(db, client.Id, ct));
        });

        clients.MapGet("/{id:guid}", async Task<Results<Ok<ClientDetail>, NotFound>> (Guid id, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var broker = await CurrentBroker(http, db, ct);
            var client = await db.Clients.VisibleTo(broker).SingleOrDefaultAsync(c => c.Id == id, ct);
            if (client is null) return TypedResults.NotFound();
            client.SeenByBrokerAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(await Detail(db, id, ct));
        });

        clients.MapPut("/{id:guid}", async Task<Results<Ok<ClientDetail>, NotFound, ValidationProblem>> (
            Guid id, BrokerClientForm body, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            if (body.Form.Validate() is { Count: > 0 } errors) return TypedResults.ValidationProblem(errors);
            var client = await Load(db, await CurrentBroker(http, db, ct), id, ct);
            if (client is null) return TypedResults.NotFound();
            client.Apply(body.Form, db);
            client.ExtraHelpLevel = Math.Clamp(body.ExtraHelpLevel, 0, 3);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(await Detail(db, id, ct));
        });

        clients.MapPut("/{id:guid}/status", async Task<Results<NoContent, NotFound>> (
            Guid id, StatusChange body, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var client = await Load(db, await CurrentBroker(http, db, ct), id, ct);
            if (client is null) return TypedResults.NotFound();
            client.Status = body.Status;
            client.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        clients.MapPost("/{id:guid}/notes", async Task<Results<Ok<NoteView>, NotFound, BadRequest<string>>> (
            Guid id, NewNote body, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Body)) return TypedResults.BadRequest("Note is empty.");
            var broker = await CurrentBroker(http, db, ct);
            var client = await Load(db, broker, id, ct);
            if (client is null) return TypedResults.NotFound();
            var note = new ClientNote { ClientId = id, AuthorId = broker.Id, Body = body.Body.Trim() };
            db.ClientNotes.Add(note);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(new NoteView(note.Id, note.Body, broker.DisplayName, note.CreatedAt));
        });

        // A personal link for this client to enter or review their own information.
        clients.MapPost("/{id:guid}/invite", async Task<Results<Ok<InviteLink>, NotFound>> (
            Guid id, HttpContext http, AppDbContext db, Notifications notify, EmailOptions email, CancellationToken ct) =>
        {
            var broker = await CurrentBroker(http, db, ct);
            var client = await Load(db, broker, id, ct);
            if (client is null) return TypedResults.NotFound();
            var token = IntakeEndpoints.NewLink(db, client, LinkPurpose.Invite);
            await db.SaveChangesAsync(ct);
            await notify.InviteAsync(client, broker, token, ct);
            return TypedResults.Ok(new InviteLink($"{email.BaseUrl}/i/{token}", DateTimeOffset.UtcNow + PatientAuth.LinkLifetime, client.Email is not null));
        });

        clients.MapPost("/{id:guid}/quote", async Task<Results<Ok<QuoteResult>, NotFound>> (
            Guid id, QuoteOptions body, HttpContext http, AppDbContext db, ClientQuotes quotes, CancellationToken ct) =>
        {
            var broker = await CurrentBroker(http, db, ct);
            var client = await Load(db, broker, id, ct);
            if (client is null) return TypedResults.NotFound();
            return TypedResults.Ok(await quotes.QuoteAsync(client, await db.CarriersAsync(broker.Id, ct), body, ct));
        });

        // Presenting or printing a quote saves exactly what was shown, with the data release behind it.
        clients.MapPost("/{id:guid}/snapshots", async Task<Results<Ok<SnapshotCreated>, NotFound>> (
            Guid id, QuoteOptions body, HttpContext http, AppDbContext db, ClientQuotes quotes, QuoteRepository repo, CancellationToken ct) =>
        {
            var broker = await CurrentBroker(http, db, ct);
            var client = await Load(db, broker, id, ct);
            if (client is null) return TypedResults.NotFound();
            var quote = await quotes.QuoteAsync(client, await db.CarriersAsync(broker.Id, ct), body, ct);
            var snapshot = new QuoteSnapshot
            {
                ClientId = id, BrokerId = broker.Id, PlanYear = quote.PlanYear,
                SpufReleaseId = await repo.ActiveReleaseIdAsync(ReleaseSource.Spuf, quote.PlanYear, ct) ?? 0,
                LandscapeReleaseId = await repo.ActiveReleaseIdAsync(ReleaseSource.Landscape, quote.PlanYear, ct),
                RequestJson = JsonSerializer.Serialize(new { client = client.ToForm(), client.ExtraHelpLevel, body }, Json),
                ResultJson = JsonSerializer.Serialize(quote, Json),
            };
            db.QuoteSnapshots.Add(snapshot);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(new SnapshotCreated(snapshot.Id));
        });

        api.MapGet("/snapshots/{id:guid}", async Task<Results<Ok<SnapshotView>, NotFound>> (
            Guid id, HttpContext http, AppDbContext db, ClientQuotes quotes, CancellationToken ct) =>
        {
            var broker = await CurrentBroker(http, db, ct);
            var snapshot = await db.QuoteSnapshots.SingleOrDefaultAsync(s => s.Id == id, ct);
            var client = snapshot is null ? null : await Load(db, broker, snapshot.ClientId, ct);
            if (snapshot is null || client is null) return TypedResults.NotFound();
            var author = await db.Users.Include(u => u.Agency).SingleAsync(u => u.Id == snapshot.BrokerId, ct);
            var disclaimer = await quotes.DisclaimerAsync(await db.CarriersAsync(author.Id, ct), client.Zip, ct);
            var quote = JsonSerializer.Deserialize<QuoteResult>(snapshot.ResultJson, Json)!;
            return TypedResults.Ok(new SnapshotView(snapshot.Id, snapshot.CreatedAt, $"{client.FirstName} {client.LastName}",
                author.DisplayName, author.Agency.Name, author.PhoneNumber, disclaimer, quote));
        }).WithTags("Clients").RequireAuthorization(Policies.Broker);

        var carriers = api.MapGroup("/carriers").WithTags("Settings").RequireAuthorization(Policies.Broker);

        carriers.MapGet("/", async (HttpContext http, AppDbContext db, QuoteRepository repo, CancellationToken ct) =>
        {
            var broker = await CurrentBroker(http, db, ct);
            var mine = await db.CarriersAsync(broker.Id, ct);
            var all = await repo.PdpSponsorsAsync(await repo.LatestPlanYearAsync(ct), ct);
            return TypedResults.Ok(all.Select(s => new CarrierChoice(s.ParentOrganization, s.Plans, mine.Contains(s.ParentOrganization))).ToList());
        });

        carriers.MapPut("/", async (CarrierSelection body, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var broker = await CurrentBroker(http, db, ct);
            db.BrokerCarriers.RemoveRange(db.BrokerCarriers.Where(c => c.BrokerId == broker.Id));
            db.BrokerCarriers.AddRange(body.ParentOrganizations.Distinct().Select(p => new BrokerCarrier { BrokerId = broker.Id, ParentOrganization = p }));
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static async Task<BrokerUser> CurrentBroker(HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var id = BrokerClaims.UserId(http.User);
        return await db.Users.Include(u => u.Agency).SingleAsync(u => u.Id == id, ct);
    }

    private static Task<Client?> Load(AppDbContext db, BrokerUser broker, Guid id, CancellationToken ct) =>
        db.Clients.VisibleTo(broker).Include(c => c.Drugs).Include(c => c.Pharmacies).SingleOrDefaultAsync(c => c.Id == id, ct);

    private static async Task<ClientDetail> Detail(AppDbContext db, Guid id, CancellationToken ct)
    {
        var c = await db.Clients.Include(x => x.Drugs).Include(x => x.Pharmacies).Include(x => x.Broker)
            .Include(x => x.Consents).Include(x => x.Notes).SingleAsync(x => x.Id == id, ct);
        var authors = await db.Users.Where(u => c.Notes.Select(n => n.AuthorId).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return new ClientDetail(c.Id, c.Status, c.Source, c.ToForm(), c.ExtraHelpLevel, c.CreatedAt, c.SubmittedAt, c.BrokerId,
            c.Broker.DisplayName,
            c.Notes.OrderByDescending(n => n.CreatedAt).Select(n => new NoteView(n.Id, n.Body, authors.GetValueOrDefault(n.AuthorId, "?"), n.CreatedAt)).ToList(),
            c.Consents.OrderByDescending(x => x.GrantedAt).Select(x => new ConsentView(x.Kind, x.TextVersion, x.GrantedAt)).ToList());
    }
}
