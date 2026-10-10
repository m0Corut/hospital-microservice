using System.Net;
using System.Net.Http.Json;
using Hastane.Chat.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Chat.Tests;

// JWT-TASARIM.md §5: Chat'in tüm uçları Patient/Doctor'a açık; Admin içerik okuyamaz (gizlilik) ve 403 alır;
// konuşmanın tarafı olmayan kullanıcı (başka hasta, başka doktor) için konuşma "yok" gibidir: 404.
public class ChatAuthorizationTests(ChatApiFactory factory) : ChatTestBase(factory), IClassFixture<ChatApiFactory>
{
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["POST api/chat/conversations"] = "Policy:PatientOnly",
        ["POST api/chat/conversations/with-patient"] = "Policy:DoctorOnly",
        ["GET api/chat/conversations"] = "Roles:Patient,Doctor",
        ["GET api/chat/unread"] = "Roles:Patient,Doctor",
        ["GET api/chat/conversations/{id}/messages"] = "Roles:Patient,Doctor",
        ["POST api/chat/conversations/{id}/messages"] = "Roles:Patient,Doctor",
        ["POST api/chat/conversations/{id}/read"] = "Roles:Patient,Doctor",
    };

    private const string UnknownId = "64b7f0f0f0f0f0f0f0f0f0f0";

    // ---- kapsam + golden tablo ----

    [Fact]
    public void EveryControllerEndpoint_DeclaresAuthorizationOrAnonymous()
    {
        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(Factory.Services);

        Assert.True(missing.Count == 0, "Yetki bilgisi olmayan uçlar: " + string.Join("; ", missing));
    }

    [Fact]
    public void EveryEndpoint_HasExactlyTheExpectedAuthorization()
    {
        var actual = new Dictionary<string, string>();
        foreach (var endpoint in Factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is null) continue;
            var verbs = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            var parts = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => a.Policy is not null ? $"Policy:{a.Policy}" : $"Roles:{a.Roles}")
                .ToList();
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null) parts = ["Anonymous"];
            foreach (var verb in verbs)
                actual[$"{verb} {endpoint.RoutePattern.RawText}"] = string.Join(" + ", parts);
        }

        Assert.Equal(Expected.OrderBy(k => k.Key).Select(k => $"{k.Key} => {k.Value}"),
            actual.OrderBy(k => k.Key).Select(k => $"{k.Key} => {k.Value}"));
    }

    // ---- 401 ----

    [Fact]
    public async Task AllEndpoints_WithoutToken_Return401()
    {
        var client = Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/chat/conversations", new StartConversationDto { DoctorId = "d" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/chat/conversations/with-patient", new StartConversationWithPatientDto { PatientUserId = "p" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/chat/conversations", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/chat/unread", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/chat/conversations/{UnknownId}/messages", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/chat/conversations/{UnknownId}/messages", new SendMessageDto { Text = "x" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/chat/conversations/{UnknownId}/read", null, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("broken-signature")]
    [InlineData("alg-none")]
    [InlineData("hs256-public-key-as-secret")]
    public async Task InvalidTokens_Return401(string kind)
    {
        var roles = new[] { HastaneRoles.Patient };
        var token = kind switch
        {
            "expired" => TestJwt.CreateExpired("p", roles),
            "broken-signature" => TestJwt.CreateWithBrokenSignature("p", roles),
            "alg-none" => TestJwt.CreateAlgNone("p", roles),
            _ => TestJwt.CreateHs256WithPublicKeyAsSecret("p", roles),
        };

        var response = await Factory.CreateClient().WithBearer(token).GetAsync("/api/chat/conversations", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- rol tablosu ----

    [Fact]
    public async Task Admin_IsForbiddenEverywhere_EvenWithExistingConversation()
    {
        var (_, _, conversation) = await OpenConversationAsync();
        var admin = AdminClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/chat/conversations", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/chat/unread", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/chat/conversations/{conversation.Id}/messages", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/chat/conversations/{conversation.Id}/messages", new SendMessageDto { Text = "x" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"/api/chat/conversations/{conversation.Id}/read", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/chat/conversations", new StartConversationDto { DoctorId = "d" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/chat/conversations/with-patient", new StartConversationWithPatientDto { PatientUserId = "p" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task StartEndpoints_AreRoleSpecific()
    {
        // Doktor "hasta olarak" açamaz, hasta "doktor olarak" açamaz.
        var doctor = DoctorClient(NewId("du"), NewId("doc"));
        var patient = PatientClient(NewId("patient"));

        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.PostAsJsonAsync("/api/chat/conversations", new StartConversationDto { DoctorId = "d" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.PostAsJsonAsync("/api/chat/conversations/with-patient", new StartConversationWithPatientDto { PatientUserId = "p" }, Ct)).StatusCode);
    }

    // ---- üçüncü kişi: 404 ----

    [Fact]
    public async Task OtherPatient_CannotReadSendOrMarkRead_Gets404_AndNothingChanges()
    {
        var (patientId, doctorId, conversation) = await OpenConversationAsync();
        var owner = PatientClient(patientId);
        var doctor = DoctorClient(NewId("du"), doctorId);
        await SendAsync(doctor, conversation.Id, "gizli teşhis notu");
        var stranger = PatientClient(NewId("patient-baska"));

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/chat/conversations/{conversation.Id}/messages", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/chat/conversations/{conversation.Id}/messages", new SendMessageDto { Text = "sızma" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/chat/conversations/{conversation.Id}/read", null, Ct)).StatusCode);

        // Sahibin tarafında değişen bir şey yok: tek mesaj, okunmamış hâlâ 1.
        Assert.Single(await GetMessagesAsync(owner, conversation.Id));
        Assert.Equal(1, (await MineAsync(owner, conversation.Id)).UnreadCount);
        // Listede de görünmez.
        Assert.Empty((await stranger.GetFromJsonAsync<List<ConversationDto>>("/api/chat/conversations", Ct))!);
    }

    [Fact]
    public async Task OtherDoctor_CannotReadSendOrMarkRead_Gets404()
    {
        var (_, _, conversation) = await OpenConversationAsync();
        var otherDoctor = DoctorClient(NewId("du"), NewId("doc-baska"));

        Assert.Equal(HttpStatusCode.NotFound, (await otherDoctor.GetAsync($"/api/chat/conversations/{conversation.Id}/messages", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherDoctor.PostAsJsonAsync($"/api/chat/conversations/{conversation.Id}/messages", new SendMessageDto { Text = "sızma" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherDoctor.PostAsync($"/api/chat/conversations/{conversation.Id}/read", null, Ct)).StatusCode);
        Assert.Empty((await otherDoctor.GetFromJsonAsync<List<ConversationDto>>("/api/chat/conversations", Ct))!);
    }

    [Fact]
    public async Task DoctorWithoutDoctorIdClaim_SeesNothing_Gets404()
    {
        var (_, _, conversation) = await OpenConversationAsync();
        var token = TestJwt.Create(NewId("du"), [HastaneRoles.Doctor]); // doctor_id yok
        var client = Factory.CreateClient().WithBearer(token);

        Assert.Empty((await client.GetFromJsonAsync<List<ConversationDto>>("/api/chat/conversations", Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/chat/conversations/{conversation.Id}/messages", Ct)).StatusCode);
        Assert.Equal(0, await UnreadTotalAsync(client));
    }

    [Fact]
    public async Task UnknownOrMalformedConversationId_Is404_SameAsStranger()
    {
        var patient = PatientClient(NewId("patient"));

        Assert.Equal(HttpStatusCode.NotFound, (await patient.GetAsync($"/api/chat/conversations/{UnknownId}/messages", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await patient.GetAsync("/api/chat/conversations/abc/messages", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await patient.PostAsJsonAsync("/api/chat/conversations/abc/messages", new SendMessageDto { Text = "x" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Patient_CannotImpersonate_ViaBody()
    {
        // Konuşma açarken kimlik token'dan: gövdede patientUserId göndermek hiçbir etki yapmaz (DTO'da alan yok).
        var patientId = NewId("patient");
        var doctorId = NewId("doc");
        Factory.Downstream.AddDoctor(doctorId, "Mehmet", "Kaya");
        Factory.Downstream.AddAppointment(patientId, doctorId);
        var body = new StringContent($$"""{"doctorId":"{{doctorId}}","patientUserId":"baska-hasta"}""", System.Text.Encoding.UTF8, "application/json");

        var response = await PatientClient(patientId).PostAsync("/api/chat/conversations", body, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(patientId, (await response.Content.ReadFromJsonAsync<ConversationDto>(Ct))!.PatientUserId);
    }
}
