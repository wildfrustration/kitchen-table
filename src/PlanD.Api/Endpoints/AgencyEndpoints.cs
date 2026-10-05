using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PlanD.Api.Data;

namespace PlanD.Api.Endpoints;

/// <summary>
/// Agency admins manage the agency's brokers: invite them, change their role, and deactivate them (handing their clients
/// to another broker). An invited broker sets up their own login at /join/{token}.
/// </summary>
public static partial class AgencyEndpoints
{
    private const string AdminKey = "agency-admin";

    public static void MapAgencyEndpoints(this RouteGroupBuilder api)
    {
        var agency = api.MapGroup("/agency").WithTags("Agency").RequireAuthorization(Policies.Broker)
            .AddEndpointFilter(RequireAdmin)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        agency.MapGet("/brokers", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var admin = Admin(http);
            var brokers = await db.Users.Where(u => u.AgencyId == admin.AgencyId)
                .OrderBy(u => u.DeactivatedAt != null).ThenBy(u => u.DisplayName)
                .Select(u => new BrokerSummary(u.Id, u.DisplayName, u.Email!, u.PhoneNumber, u.Role, u.PublicSlug,
                    db.Clients.Count(c => c.BrokerId == u.Id),
                    db.Clients.Count(c => c.BrokerId == u.Id && c.Status != ClientStatus.Enrolled && c.Status != ClientStatus.Closed),
                    u.Carriers.Count, u.CreatedAt, u.LastSignInAt, u.DeactivatedAt, u.Id == admin.Id))
                .ToListAsync(ct);
            return TypedResults.Ok(brokers);
        });

        agency.MapPut("/brokers/{id:guid}/role", async Task<Results<NoContent, NotFound, BadRequest<string>>> (
            Guid id, RoleChange body, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            // Admins can't demote or deactivate themselves, so an agency always keeps at least one admin.
            var admin = Admin(http);
            if (id == admin.Id) return TypedResults.BadRequest("You can't change your own role. Ask another admin.");
            var broker = await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.AgencyId == admin.AgencyId, ct);
            if (broker is null) return TypedResults.NotFound();
            broker.Role = body.Role;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        agency.MapPost("/brokers/{id:guid}/deactivate", async Task<Results<NoContent, NotFound, BadRequest<string>>> (
            Guid id, Deactivation body, HttpContext http, AppDbContext db, UserManager<BrokerUser> users, CancellationToken ct) =>
        {
            var admin = Admin(http);
            if (id == admin.Id) return TypedResults.BadRequest("You can't deactivate your own account.");
            var broker = await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.AgencyId == admin.AgencyId, ct);
            if (broker is null) return TypedResults.NotFound();
            if (broker.DeactivatedAt is not null) return TypedResults.NoContent();

            // Their clients go to someone who can still work them, with a note on each client saying so.
            var clients = await db.Clients.Where(c => c.BrokerId == id).ToListAsync(ct);
            if (clients.Count > 0)
            {
                var target = body.MoveClientsTo is { } to && to != id
                    ? await db.Users.SingleOrDefaultAsync(u => u.Id == to && u.AgencyId == admin.AgencyId && u.DeactivatedAt == null, ct)
                    : null;
                if (target is null) return TypedResults.BadRequest($"Choose who takes over {broker.DisplayName}'s clients.");
                foreach (var c in clients)
                {
                    c.BrokerId = target.Id;
                    db.ClientNotes.Add(new ClientNote
                    {
                        ClientId = c.Id, AuthorId = admin.Id,
                        Body = $"Moved from {broker.DisplayName} to {target.DisplayName} when {broker.DisplayName}'s account was deactivated.",
                    });
                }
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            broker.DeactivatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            // A new security stamp ends their open sessions on the next request.
            var stamped = await users.UpdateSecurityStampAsync(broker);
            if (!stamped.Succeeded) throw new InvalidOperationException(string.Join("; ", stamped.Errors.Select(e => e.Description)));
            await tx.CommitAsync(ct);
            return TypedResults.NoContent();
        });

        agency.MapPost("/brokers/{id:guid}/reactivate", async Task<Results<NoContent, NotFound>> (
            Guid id, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var admin = Admin(http);
            var broker = await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.AgencyId == admin.AgencyId, ct);
            if (broker is null) return TypedResults.NotFound();
            broker.DeactivatedAt = null;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        agency.MapGet("/invites", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var agencyId = Admin(http).AgencyId;
            var invites = await (
                from i in db.BrokerInvites
                join u in db.Users on i.InvitedById equals u.Id
                where i.AgencyId == agencyId && i.AcceptedAt == null
                orderby i.CreatedAt descending
                select new PendingInvite(i.Id, i.Email, i.DisplayName, i.Role, u.DisplayName, i.CreatedAt, i.ExpiresAt)).ToListAsync(ct);
            return TypedResults.Ok(invites);
        });

        agency.MapPost("/invites", async Task<Results<Ok<InviteLink>, ValidationProblem>> (
            NewBrokerInvite body, HttpContext http, AppDbContext db, UserManager<BrokerUser> users, Notifications notify,
            EmailOptions email, CancellationToken ct) =>
        {
            var admin = Admin(http);
            var address = body.Email.Trim().ToLowerInvariant();
            var name = body.DisplayName.Trim();
            var errors = new Dictionary<string, string[]>();
            if (name.Length == 0) errors[nameof(body.DisplayName)] = ["Enter their name."];
            if (!address.Contains('@')) errors[nameof(body.Email)] = ["That email doesn't look right."];
            else if (await users.FindByEmailAsync(address) is not null) errors[nameof(body.Email)] = [$"{address} already has a Kitchen Table login."];
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

            // Inviting the same address again replaces the earlier invite, so only the newest link works.
            await db.BrokerInvites.Where(i => i.AgencyId == admin.AgencyId && i.Email == address && i.AcceptedAt == null).ExecuteDeleteAsync(ct);
            var invite = new BrokerInvite
            {
                AgencyId = admin.AgencyId, Email = address, DisplayName = name, Role = body.Role, InvitedById = admin.Id, TokenHash = "",
            };
            var token = Renew(invite);
            db.BrokerInvites.Add(invite);
            await db.SaveChangesAsync(ct);
            await notify.BrokerInviteAsync(invite, admin, token, ct);
            return TypedResults.Ok(new InviteLink($"{email.BaseUrl}/join/{token}", invite.ExpiresAt, true));
        });

        agency.MapPost("/invites/{id:guid}/resend", async Task<Results<Ok<InviteLink>, NotFound>> (
            Guid id, HttpContext http, AppDbContext db, Notifications notify, EmailOptions email, CancellationToken ct) =>
        {
            var admin = Admin(http);
            var invite = await db.BrokerInvites.SingleOrDefaultAsync(i => i.Id == id && i.AgencyId == admin.AgencyId && i.AcceptedAt == null, ct);
            if (invite is null) return TypedResults.NotFound();
            invite.InvitedById = admin.Id;
            var token = Renew(invite);
            await db.SaveChangesAsync(ct);
            await notify.BrokerInviteAsync(invite, admin, token, ct);
            return TypedResults.Ok(new InviteLink($"{email.BaseUrl}/join/{token}", invite.ExpiresAt, true));
        });

        agency.MapDelete("/invites/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var agencyId = Admin(http).AgencyId;
            var deleted = await db.BrokerInvites.Where(i => i.Id == id && i.AgencyId == agencyId && i.AcceptedAt == null).ExecuteDeleteAsync(ct);
            return deleted > 0 ? TypedResults.NoContent() : TypedResults.NotFound();
        });

        // The invited broker's side: no session yet, just the token from the email.
        var join = api.MapGroup("/join").WithTags("Join");

        join.MapPost("/lookup", async Task<Results<Ok<JoinInvite>, NotFound>> (JoinLookup body, AppDbContext db, CancellationToken ct) =>
        {
            var hash = Tokens.Hash(body.Token);
            var now = DateTimeOffset.UtcNow;
            var invite = await (
                from i in db.BrokerInvites
                join a in db.Agencies on i.AgencyId equals a.Id
                join u in db.Users on i.InvitedById equals u.Id
                where i.TokenHash == hash && i.AcceptedAt == null && i.ExpiresAt > now
                select new JoinInvite(a.Name, i.Email, i.DisplayName, i.Role, u.DisplayName)).SingleOrDefaultAsync(ct);
            return invite is null ? TypedResults.NotFound() : TypedResults.Ok(invite);
        });

        join.MapPost("/", async Task<Results<NoContent, NotFound, ValidationProblem>> (
            JoinRequest body, AppDbContext db, UserManager<BrokerUser> users, SignInManager<BrokerUser> signIn, CancellationToken ct) =>
        {
            var hash = Tokens.Hash(body.Token);
            var now = DateTimeOffset.UtcNow;
            var invite = await db.BrokerInvites.SingleOrDefaultAsync(i => i.TokenHash == hash && i.AcceptedAt == null && i.ExpiresAt > now, ct);
            if (invite is null) return TypedResults.NotFound();

            var name = body.DisplayName.Trim();
            if (name.Length == 0) return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(body.DisplayName)] = ["Enter your name."] });

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var user = new BrokerUser
            {
                AgencyId = invite.AgencyId, UserName = invite.Email, Email = invite.Email, EmailConfirmed = true,
                DisplayName = name, Role = invite.Role, PublicSlug = await UniqueSlugAsync(db, name, ct),
                PhoneNumber = string.IsNullOrWhiteSpace(body.Phone) ? null : body.Phone.Trim(), LastSignInAt = now,
            };
            var created = await users.CreateAsync(user, body.Password);
            if (!created.Succeeded)
                return TypedResults.ValidationProblem(created.Errors
                    .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? nameof(body.Password) : "Email")
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
            invite.AcceptedAt = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await signIn.SignInAsync(user, isPersistent: false);
            return TypedResults.NoContent();
        });
    }

    private static async ValueTask<object?> RequireAdmin(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        // The role is read on every request, so a demotion takes effect at once.
        var http = ctx.HttpContext;
        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        var id = BrokerClaims.UserId(http.User);
        var admin = await db.Users.Include(u => u.Agency)
            .SingleOrDefaultAsync(u => u.Id == id && u.Role == BrokerRole.AgencyAdmin && u.DeactivatedAt == null, http.RequestAborted);
        if (admin is null) return TypedResults.Problem("Only agency admins can manage brokers.", statusCode: StatusCodes.Status403Forbidden);
        http.Items[AdminKey] = admin;
        return await next(ctx);
    }

    private static BrokerUser Admin(HttpContext http) => (BrokerUser)http.Items[AdminKey]!;

    /// <summary>Gives the invite a fresh token and expiry; returns the raw token (only its hash is stored).</summary>
    private static string Renew(BrokerInvite invite)
    {
        var token = Tokens.New();
        invite.TokenHash = Tokens.Hash(token);
        invite.CreatedAt = DateTimeOffset.UtcNow;
        invite.ExpiresAt = invite.CreatedAt + BrokerInvite.Lifetime;
        return token;
    }

    /// <summary>"José Núñez" → "jose-nunez" for the public link, with -2, -3… when that's taken.</summary>
    private static async Task<string> UniqueSlugAsync(AppDbContext db, string name, CancellationToken ct)
    {
        var ascii = new string(name.Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
        var slug = NotSlug().Replace(ascii.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        if (slug.Length == 0) slug = "broker";

        var taken = await db.Users.Where(u => u.PublicSlug == slug || u.PublicSlug.StartsWith(slug + "-"))
            .Select(u => u.PublicSlug).ToHashSetAsync(ct);
        var candidate = slug;
        for (var n = 2; taken.Contains(candidate); n++) candidate = $"{slug}-{n}";
        return candidate;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlug();
}
