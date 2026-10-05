using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlanD.Api.Data;
using PlanD.Api.Endpoints;

namespace PlanD.Api.Tests;

/// <summary>An agency admin invites, promotes and deactivates brokers; brokers can't do any of it.</summary>
public sealed class AgencyTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private const string NewPassword = "a-new-broker-password";

    [Fact]
    public async Task Invited_broker_sets_up_a_login_and_joins_the_agency()
    {
        var admin = await SignIn(await api.ResetWithBrokerAsync());
        var token = await Invite(admin, "jamie@test.example.com", "Jamie Cruz", BrokerRole.Broker);

        var anonymous = api.CreateClient();
        var lookup = await Read<JoinInvite>(await anonymous.PostAsJsonAsync("/api/join/lookup", new JoinLookup(token), Json));
        Assert.Equal(("Test Agency", "jamie@test.example.com", "Test Broker"), (lookup.AgencyName, lookup.Email, lookup.InvitedBy));

        var tooShort = await anonymous.PostAsJsonAsync("/api/join", new JoinRequest(token, "Jamie Cruz", null, "short"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/join", new JoinRequest(token, "Jamie Cruz", "555 010 0199", NewPassword), Json)).StatusCode);

        // Signed in straight away, and the link is spent.
        var me = await Read<Me>(await anonymous.GetAsync("/api/auth/me"));
        Assert.Equal((BrokerRole.Broker, "jamie-cruz", "Test Agency"), (me.Role, me.PublicSlug, me.Agency.Name));
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().PostAsJsonAsync("/api/join/lookup", new JoinLookup(token), Json)).StatusCode);

        var brokers = await Read<List<BrokerSummary>>(await admin.GetAsync("/api/agency/brokers"));
        Assert.Equal(["Jamie Cruz", "Test Broker"], brokers.Select(b => b.DisplayName));
        Assert.Empty(await Read<List<PendingInvite>>(await admin.GetAsync("/api/agency/invites")));
    }

    [Fact]
    public async Task Inviting_an_address_again_replaces_the_earlier_link()
    {
        var admin = await SignIn(await api.ResetWithBrokerAsync());
        var first = await Invite(admin, "jamie@test.example.com", "Jamie", BrokerRole.Broker);
        var second = await Invite(admin, "Jamie@Test.example.com ", "Jamie Cruz", BrokerRole.AgencyAdmin);

        var pending = Assert.Single(await Read<List<PendingInvite>>(await admin.GetAsync("/api/agency/invites")));
        Assert.Equal(("Jamie Cruz", BrokerRole.AgencyAdmin), (pending.DisplayName, pending.Role));
        var anonymous = api.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync("/api/join/lookup", new JoinLookup(first), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/join/lookup", new JoinLookup(second), Json)).StatusCode);

        var existing = await admin.PostAsJsonAsync("/api/agency/invites", new NewBrokerInvite("broker@test.example.com", "Again", BrokerRole.Broker), Json);
        Assert.Equal(HttpStatusCode.BadRequest, existing.StatusCode);
    }

    [Fact]
    public async Task Brokers_cannot_manage_the_agency_until_promoted()
    {
        var admin = await SignIn(await api.ResetWithBrokerAsync());
        var (broker, brokerId) = await Join(admin, "jamie@test.example.com", "Jamie Cruz");

        Assert.Equal(HttpStatusCode.Forbidden, (await broker.GetAsync("/api/agency/brokers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await broker.PostAsJsonAsync("/api/agency/invites", new NewBrokerInvite("x@test.example.com", "X", BrokerRole.Broker), Json)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/agency/brokers/{brokerId}/role", new RoleChange(BrokerRole.AgencyAdmin), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await broker.GetAsync("/api/agency/brokers")).StatusCode);
    }

    [Fact]
    public async Task Admins_cannot_demote_or_deactivate_themselves()
    {
        var admin = await SignIn(await api.ResetWithBrokerAsync());
        var me = await Read<Me>(await admin.GetAsync("/api/auth/me"));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/agency/brokers/{me.Id}/role", new RoleChange(BrokerRole.Broker), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/agency/brokers/{me.Id}/deactivate", new Deactivation(null), Json)).StatusCode);
    }

    [Fact]
    public async Task Deactivating_a_broker_hands_over_their_clients_and_signs_them_out()
    {
        var admin = await SignIn(await api.ResetWithBrokerAsync());
        var adminId = (await Read<Me>(await admin.GetAsync("/api/auth/me"))).Id;
        var (broker, brokerId) = await Join(admin, "jamie@test.example.com", "Jamie Cruz");
        var form = new IntakeForm("Ada", "Lovelace", "ada@example.com", null, "33135", null, 3, 1958, null, null, ExtraHelpAnswer.No, false, [], []);
        var client = await Read<ClientDetail>(await broker.PostAsJsonAsync("/api/clients", new BrokerClientForm(form, 0), Json));

        // Someone has to take over the clients.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/agency/brokers/{brokerId}/deactivate", new Deactivation(null), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/agency/brokers/{brokerId}/deactivate", new Deactivation(adminId), Json)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await broker.GetAsync("/api/auth/me")).StatusCode);
        var login = await api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("jamie@test.example.com", NewPassword, false), Json);
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync("/api/intake/jamie-cruz")).StatusCode);

        var moved = await Read<ClientDetail>(await admin.GetAsync($"/api/clients/{client.Id}"));
        Assert.Equal(adminId, moved.BrokerId);
        Assert.Contains("deactivated", Assert.Single(moved.Notes).Body);
        var listed = (await Read<List<BrokerSummary>>(await admin.GetAsync("/api/agency/brokers"))).Single(b => b.Id == brokerId);
        Assert.Equal(0, listed.Clients);
        Assert.NotNull(listed.DeactivatedAt);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/agency/brokers/{brokerId}/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SignInResponse("jamie@test.example.com", NewPassword)).StatusCode);
    }

    private async Task<string> Invite(HttpClient admin, string email, string name, BrokerRole role)
    {
        var link = await Read<InviteLink>(await admin.PostAsJsonAsync("/api/agency/invites", new NewBrokerInvite(email, name, role), Json));
        return link.Url[(link.Url.LastIndexOf('/') + 1)..];
    }

    /// <summary>Invites and joins a broker; returns their signed-in client and id.</summary>
    private async Task<(HttpClient Client, Guid Id)> Join(HttpClient admin, string email, string name)
    {
        var token = await Invite(admin, email, name, BrokerRole.Broker);
        var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/join", new JoinRequest(token, name, null, NewPassword), Json)).StatusCode);
        return (client, (await Read<Me>(await client.GetAsync("/api/auth/me"))).Id);
    }

    private async Task<HttpClient> SignIn(string email)
    {
        var client = api.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, ApiFactory.Password, false), Json);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private Task<HttpResponseMessage> SignInResponse(string email, string password) =>
        api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, false), Json);

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }
}
