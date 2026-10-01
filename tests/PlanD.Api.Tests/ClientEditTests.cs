using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlanD.Api.Endpoints;

namespace PlanD.Api.Tests;

/// <summary>Saving a client replaces its drug and pharmacy lists — for brokers and for patients on a return link.</summary>
public sealed class ClientEditTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static IntakeForm Form(params DrugEntry[] drugs) => new(
        "Ada", "Lovelace", "ada@example.com", null, "33135", null, 3, 1958, null, null,
        Data.ExtraHelpAnswer.No, false, drugs, [new PharmacyEntry("1013922236", "Walgreens #03316", "2700 W Flagler St, Miami", "33135")]);

    private static readonly DrugEntry Atorvastatin = new("617310", "atorvastatin 20 MG Oral Tablet", 1, 1, 30);
    private static readonly DrugEntry Metformin = new("861007", "metformin hydrochloride 500 MG Oral Tablet", 1, 2, 30);

    [Fact]
    public async Task Broker_can_edit_a_client_and_replace_its_drugs_and_pharmacies()
    {
        var client = await SignedInBroker();
        var created = await Read<ClientDetail>(await client.PostAsJsonAsync("/api/clients", new BrokerClientForm(Form(Atorvastatin), 0), Json));

        // Unchanged save, then a save that adds a drug and swaps the pharmacy.
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/clients/{created.Id}", new BrokerClientForm(created.Form, 0), Json)).StatusCode);
        var changed = created.Form with
        {
            Drugs = [Atorvastatin, Metformin],
            Pharmacies = [new PharmacyEntry("1174538342", "Walgreens #04809", "3595 Coral Way, Miami", "33135")],
            CurrentPlan = "SilverScript Choice",
        };
        var saved = await Read<ClientDetail>(await client.PutAsJsonAsync($"/api/clients/{created.Id}", new BrokerClientForm(changed, 1), Json));

        Assert.Equal(["617310", "861007"], saved.Form.Drugs.Select(d => d.Rxcui));
        Assert.Equal("1174538342", Assert.Single(saved.Form.Pharmacies).Npi);
        Assert.Equal("SilverScript Choice", saved.Form.CurrentPlan);
        Assert.Equal(1, saved.ExtraHelpLevel);
    }

    [Fact]
    public async Task Patient_can_update_their_intake_from_an_invite_link()
    {
        var broker = await SignedInBroker();
        var created = await Read<ClientDetail>(await broker.PostAsJsonAsync("/api/clients", new BrokerClientForm(Form(Atorvastatin), 0), Json));
        var invite = await Read<InviteLink>(await broker.PostAsync($"/api/clients/{created.Id}/invite", null));

        var patient = api.CreateClient();
        var token = invite.Url[(invite.Url.LastIndexOf('/') + 1)..];
        Assert.Equal(HttpStatusCode.OK, (await patient.PostAsJsonAsync("/api/patient/redeem", new RedeemRequest(token), Json)).StatusCode);
        var update = new PublicIntake(created.Form with { Drugs = [Metformin] }, true, true);
        Assert.Equal(HttpStatusCode.NoContent, (await patient.PutAsJsonAsync("/api/patient/intake", update, Json)).StatusCode);

        var after = await Read<ClientDetail>(await broker.GetAsync($"/api/clients/{created.Id}"));
        Assert.Equal("861007", Assert.Single(after.Form.Drugs).Rxcui);
        Assert.Equal(2, after.Consents.Count);
        Assert.NotNull(after.SubmittedAt);
    }

    private async Task<HttpClient> SignedInBroker()
    {
        var email = await api.ResetWithBrokerAsync();
        var client = api.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, ApiFactory.Password, false), Json);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }
}
