using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PlanD.Api;
using PlanD.Api.Data;
using PlanD.Api.Endpoints;
using PlanD.Data;
using PlanD.Engine.Quoting;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

// --- data -------------------------------------------------------------------------------------
services.AddSingleton(_ => Db.CreateDataSource());
services.AddDbContext<AppDbContext>(o => o.UseNpgsql(Db.ConnectionString,
    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", AppDbContext.Schema)));
services.AddSingleton<QuoteRepository>();
services.AddSingleton<IQuoteData>(sp => sp.GetRequiredService<QuoteRepository>());
services.AddSingleton<QuoteService>();
services.AddSingleton<ClientQuotes>();

// --- email: logged in development, Resend in production ------------------------------------------
var emailOptions = builder.Configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
services.AddSingleton(emailOptions);
if (emailOptions.Provider.Equals("resend", StringComparison.OrdinalIgnoreCase))
    services.AddHttpClient<IEmailSender, ResendEmailSender>();
else
    services.AddSingleton<IEmailSender, LogEmailSender>();
services.AddSingleton<Notifications>();

// --- auth: brokers sign in with Identity; patients get a separate, narrower cookie ---------------
services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, o => ConfigureApiCookie(o, "kt_broker", TimeSpan.FromHours(12), sliding: true))
    .AddCookie(PatientAuth.Scheme, o => ConfigureApiCookie(o, "kt_patient", PatientAuth.SessionLength, sliding: false));
services.AddIdentityCore<BrokerUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 10;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireDigit = false;
        o.Lockout.MaxFailedAccessAttempts = 10;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Broker, p => p.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser())
    .AddPolicy(Policies.Patient, p => p.AddAuthenticationSchemes(PatientAuth.Scheme).RequireAuthenticatedUser());

// --- API --------------------------------------------------------------------------------------
services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
services.AddOpenApi();
services.AddProblemDetails();

var app = builder.Build();

if (args is ["migrate", ..])
{
    await using var scope = app.Services.CreateAsyncScope();
    await Migrator.MigrateAsync(scope.ServiceProvider.GetRequiredService<Npgsql.NpgsqlDataSource>());
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    Console.WriteLine("Migrated cms and app schemas.");
    return;
}
if (args is ["demo-reset", ..])
{
    await DemoSeed.ResetAsync(app.Services);
    return;
}

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
var api = app.MapGroup("/api");
api.MapAuthEndpoints();
api.MapReferenceEndpoints();
api.MapIntakeEndpoints();
api.MapClientEndpoints();
app.MapFallbackToFile("index.html");

app.Run();

// An API cookie answers 401/403 instead of redirecting to a login page.
static void ConfigureApiCookie(CookieAuthenticationOptions o, string name, TimeSpan lifetime, bool sliding)
{
    o.Cookie.Name = name;
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = lifetime;
    o.SlidingExpiration = sliding;
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
}

public partial class Program;
