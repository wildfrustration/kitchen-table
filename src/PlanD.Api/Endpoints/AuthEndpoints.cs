using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PlanD.Api.Data;

namespace PlanD.Api.Endpoints;

public sealed record LoginRequest(string Email, string Password, bool RememberMe);

public sealed record AgencyInfo(Guid Id, string Name);

public sealed record Me(Guid Id, string Email, string DisplayName, BrokerRole Role, string PublicSlug, AgencyInfo Agency);

public static class AuthEndpoints
{
    private static readonly TimeSpan Session = TimeSpan.FromHours(12);
    private static readonly TimeSpan RememberedSession = TimeSpan.FromDays(14);

    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/login", async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> (
            LoginRequest body, UserManager<BrokerUser> users, SignInManager<BrokerUser> signIn) =>
        {
            var user = await users.FindByEmailAsync(body.Email.Trim());
            if (user is null) return TypedResults.Unauthorized();

            var check = await signIn.CheckPasswordSignInAsync(user, body.Password, lockoutOnFailure: true);
            if (!check.Succeeded) return TypedResults.Unauthorized();
            // Only said after a correct password, so it can't be used to find out who has an account.
            if (user.DeactivatedAt is not null)
                return TypedResults.Problem("Your account was deactivated. Ask your agency admin if you need access again.", statusCode: StatusCodes.Status403Forbidden);

            user.LastSignInAt = DateTimeOffset.UtcNow;
            await users.UpdateAsync(user);

            await signIn.SignInAsync(user, new AuthenticationProperties
            {
                IsPersistent = body.RememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow + (body.RememberMe ? RememberedSession : Session),
            });
            return TypedResults.NoContent();
        });

        auth.MapPost("/logout", async (SignInManager<BrokerUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return TypedResults.NoContent();
        });

        auth.MapGet("/me", async Task<Results<Ok<Me>, UnauthorizedHttpResult>> (HttpContext http, AppDbContext db) =>
        {
            var id = BrokerClaims.UserId(http.User);
            var me = await db.Users.Where(u => u.Id == id)
                .Select(u => new Me(u.Id, u.Email!, u.DisplayName, u.Role, u.PublicSlug, new AgencyInfo(u.Agency.Id, u.Agency.Name)))
                .SingleOrDefaultAsync();
            return me is null ? TypedResults.Unauthorized() : TypedResults.Ok(me);
        }).RequireAuthorization(Policies.Broker);
    }
}
