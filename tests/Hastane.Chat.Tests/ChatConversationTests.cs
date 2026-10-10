using System.Net;
using System.Net.Http.Json;
using Hastane.Chat.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Chat.Tests;

// Konuşma açma/listeleme: randevu kuralı, tekillik, doktor tarafından başlatma, kapalı servis.
public class ChatConversationTests(ChatApiFactory factory) : ChatTestBase(factory), IClassFixture<ChatApiFactory>
{
    [Fact]
    public async Task Patient_WithAppointment_OpensConversation_201_WithNamesFromTokenAndDoctor()
    {
        var (patientId, doctorId, conversation) = await OpenConversationAsync("Ayşe Hasta");

        Assert.Equal(patientId, conversation.PatientUserId);
        Assert.Equal("Ayşe Hasta", conversation.PatientName); // token "name"
        Assert.Equal(doctorId, conversation.DoctorId);
        Assert.Equal("Dr. Mehmet Kaya", conversation.DoctorName); // unvan + ad (Doctor /public)
        Assert.Equal(0, conversation.UnreadCount);
        Assert.Equal(conversation.CreatedAt, conversation.LastMessageAt);
    }

    [Fact]
    public async Task Patient_WithoutAppointment_Returns403()
    {
        var doctorId = NewId("doc");
        Factory.Downstream.AddDoctor(doctorId, "Mehmet", "Kaya");

        var response = await PatientClient(NewId("patient")).PostAsJsonAsync("/api/chat/conversations", new StartConversationDto { DoctorId = doctorId }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Yalnız randevunuz olan doktora yazabilirsiniz", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Patient_OpeningSameConversationTwice_Returns200_AndSameId()
    {
        var (patientId, doctorId, first) = await OpenConversationAsync();

        var again = await PatientClient(patientId).PostAsJsonAsync("/api/chat/conversations", new StartConversationDto { DoctorId = doctorId }, Ct);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(first.Id, (await again.Content.ReadFromJsonAsync<ConversationDto>(Ct))!.Id);
        var list = await PatientClient(patientId).GetFromJsonAsync<List<ConversationDto>>("/api/chat/conversations", Ct);
        Assert.Single(list!); // tekil indeks: ikinci belge yok
    }

    [Fact]
    public async Task Patient_ParallelOpens_YieldSingleConversation()
    {
        var patientId = NewId("patient");
        var doctorId = NewId("doc");
        Factory.Downstream.AddDoctor(doctorId, "Mehmet", "Kaya");
        Factory.Downstream.AddAppointment(patientId, doctorId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            PatientClient(patientId).PostAsJsonAsync("/api/chat/conversations", new StartConversationDto { DoctorId = doctorId }, Ct)));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.Single(await PatientClient(patientId).GetFromJsonAsync<List<ConversationDto>>("/api/chat/conversations", Ct) ?? []);
    }

    [Fact]
    public async Task Patient_UnknownDoctorWithAppointment_Returns404()
    {
        var patientId = NewId("patient");
        var doctorId = NewId("doc-yok");
        Factory.Downstream.AddAppointment(patientId, doctorId); // randevu var ama doktor kaydı yok

        var response = await PatientClient(patientId).PostAsJsonAsync("/api/chat/conversations", new StartConversationDto { DoctorId = doctorId }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patient_WhenAppointmentServiceDown_Returns503()
    {
        var patientId = NewId("patient");
        var doctorId = NewId("doc");
        Factory.Downstream.AddDoctor(doctorId, "Mehmet", "Kaya");
        Factory.Downstream.AddAppointment(patientId, doctorId);
        Factory.Downstream.AppointmentDown = true;
        try
        {
            var response = await PatientClient(patientId).PostAsJsonAsync("/api/chat/conversations", new StartConversationDto { DoctorId = doctorId }, Ct);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            Factory.Downstream.AppointmentDown = false;
        }
    }

    [Fact]
    public async Task Patient_EmptyDoctorId_Returns400()
    {
        var response = await PatientClient(NewId("patient")).PostAsJsonAsync("/api/chat/conversations", new StartConversationDto { DoctorId = "  " }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Doctor_WithAppointment_StartsConversation_201_PatientNameFromIdentity()
    {
        var patientId = NewId("patient");
        var doctorId = NewId("doc");
        Factory.Downstream.AddDoctor(doctorId, "Zeynep", "Aydın", "Uzm. Dr.");
        Factory.Downstream.AddAppointment(patientId, doctorId);
        Factory.Downstream.AddUser(patientId, "Ali", "Veli");

        var response = await DoctorClient(NewId("doctor-user"), doctorId)
            .PostAsJsonAsync("/api/chat/conversations/with-patient", new StartConversationWithPatientDto { PatientUserId = patientId }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var conversation = (await response.Content.ReadFromJsonAsync<ConversationDto>(Ct))!;
        Assert.Equal("Ali Veli", conversation.PatientName);
        Assert.Equal("Uzm. Dr. Zeynep Aydın", conversation.DoctorName);

        // Aynı çift hasta tarafından açılmaya çalışılırsa aynı konuşma (200).
        var fromPatient = await PatientClient(patientId).PostAsJsonAsync("/api/chat/conversations", new StartConversationDto { DoctorId = doctorId }, Ct);
        Assert.Equal(HttpStatusCode.OK, fromPatient.StatusCode);
        Assert.Equal(conversation.Id, (await fromPatient.Content.ReadFromJsonAsync<ConversationDto>(Ct))!.Id);
    }

    [Fact]
    public async Task Doctor_WhenIdentityDown_StillOpens_WithFallbackPatientName()
    {
        var patientId = NewId("patient");
        var doctorId = NewId("doc");
        Factory.Downstream.AddDoctor(doctorId, "Zeynep", "Aydın");
        Factory.Downstream.AddAppointment(patientId, doctorId);
        Factory.Downstream.IdentityDown = true;
        try
        {
            var response = await DoctorClient(NewId("doctor-user"), doctorId)
                .PostAsJsonAsync("/api/chat/conversations/with-patient", new StartConversationWithPatientDto { PatientUserId = patientId }, Ct);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("Hasta", (await response.Content.ReadFromJsonAsync<ConversationDto>(Ct))!.PatientName);
        }
        finally
        {
            Factory.Downstream.IdentityDown = false;
        }
    }

    [Fact]
    public async Task Doctor_WithoutAppointmentWithThatPatient_Returns403()
    {
        var doctorId = NewId("doc");
        Factory.Downstream.AddDoctor(doctorId, "Zeynep", "Aydın");

        var response = await DoctorClient(NewId("doctor-user"), doctorId)
            .PostAsJsonAsync("/api/chat/conversations/with-patient", new StartConversationWithPatientDto { PatientUserId = NewId("patient") }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Doctor_WithoutDoctorIdClaim_StartWithPatient_Returns403()
    {
        var token = TestJwt.Create(NewId("doctor-user"), [HastaneRoles.Doctor]); // doctor_id yok
        var client = Factory.CreateClient().WithBearer(token);

        var response = await client.PostAsJsonAsync("/api/chat/conversations/with-patient", new StartConversationWithPatientDto { PatientUserId = "p" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_ShowsOnlyMyConversations_NewestMessageFirst_WithMyUnread()
    {
        var patientId = NewId("patient");
        var doctorA = NewId("docA");
        var doctorB = NewId("docB");
        foreach (var d in new[] { doctorA, doctorB })
        {
            Factory.Downstream.AddDoctor(d, "Dr", d);
            Factory.Downstream.AddAppointment(patientId, d);
        }
        var patient = PatientClient(patientId);
        var a = (await (await patient.PostAsJsonAsync("/api/chat/conversations", new StartConversationDto { DoctorId = doctorA }, Ct)).Content.ReadFromJsonAsync<ConversationDto>(Ct))!;
        var b = (await (await patient.PostAsJsonAsync("/api/chat/conversations", new StartConversationDto { DoctorId = doctorB }, Ct)).Content.ReadFromJsonAsync<ConversationDto>(Ct))!;
        await Task.Delay(20, Ct);
        await SendAsync(DoctorClient(NewId("du"), doctorA), a.Id, "A doktorundan merhaba");

        var list = (await patient.GetFromJsonAsync<List<ConversationDto>>("/api/chat/conversations", Ct))!;

        Assert.Equal([a.Id, b.Id], list.Select(c => c.Id).ToArray()); // a'ya mesaj geldi: üstte
        Assert.Equal(1, list[0].UnreadCount);
        Assert.Equal(0, list[1].UnreadCount);
        Assert.Equal("A doktorundan merhaba", list[0].LastMessagePreview);

        // Doktor A yalnız kendi konuşmasını görür; kendi tarafında okunmamış yok.
        var doctorList = (await DoctorClient(NewId("du"), doctorA).GetFromJsonAsync<List<ConversationDto>>("/api/chat/conversations", Ct))!;
        Assert.Equal([a.Id], doctorList.Select(c => c.Id).ToArray());
        Assert.Equal(0, doctorList[0].UnreadCount);
    }
}
