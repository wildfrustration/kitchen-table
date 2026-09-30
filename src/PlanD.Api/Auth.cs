using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace PlanD.Api;

public static class Policies
{
    public const string Broker = "broker";
    public const string Patient = "patient";
}

/// <summary>Patients never get a password: a single-use link opens a short cookie session scoped to one client.</summary>
public static class PatientAuth
{
    public const string Scheme = "Patient";
    public const string ClientIdClaim = "client_id";

    public static readonly TimeSpan SessionLength = TimeSpan.FromDays(7);
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(24);

    public static Guid ClientId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClientIdClaim) ?? throw new InvalidOperationException("Not a patient session."));
}

public static class Tokens
{
    /// <summary>A URL-safe random token (256 bits). Only its hash is stored.</summary>
    public static string New() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public static class BrokerClaims
{
    public static Guid UserId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Not a broker session."));
}
