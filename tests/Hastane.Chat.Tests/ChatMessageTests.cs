using System.Net;
using System.Net.Http.Json;
using Hastane.Chat.Api.Dtos;

namespace Hastane.Chat.Tests;

// Mesaj gönderme/okuma, `after` yoklaması, okunmamış sayaçları ve metin sınırları.
public class ChatMessageTests(ChatApiFactory factory) : ChatTestBase(factory), IClassFixture<ChatApiFactory>
{
    private static string Iso(DateTime utc) => Uri.EscapeDataString(utc.ToString("O"));

    [Fact]
    public async Task SendAndRead_BothSides_SeeMessagesInOrder_WithSenderRoleAndName()
    {
        var (patientId, doctorId, conversation) = await OpenConversationAsync("Ayşe Hasta");
        var patient = PatientClient(patientId, "Ayşe Hasta");
        var doctor = DoctorClient(NewId("du"), doctorId);

        var first = await SendAsync(patient, conversation.Id, "  Merhaba doktor  ");
        await Task.Delay(20, Ct);
        var second = await SendAsync(doctor, conversation.Id, "Merhaba, buyurun");

        Assert.Equal("Patient", first.SenderRole);
        Assert.Equal("Ayşe Hasta", first.SenderName);
        Assert.Equal("Merhaba doktor", first.Text); // kırpıldı
        Assert.Equal("Doctor", second.SenderRole);
        Assert.Equal("Dr. Mehmet Kaya", second.SenderName);

        foreach (var side in new[] { patient, doctor })
        {
            var messages = await GetMessagesAsync(side, conversation.Id);
            Assert.Equal([first.Id, second.Id], messages.Select(m => m.Id).ToArray());
        }
    }

    [Fact]
    public async Task Messages_AreStoredRaw_NotHtmlEncoded()
    {
        var (patientId, _, conversation) = await OpenConversationAsync();
        var patient = PatientClient(patientId);

        var sent = await SendAsync(patient, conversation.Id, "<script>alert(1)</script> & \"tırnak\"");

        Assert.Equal("<script>alert(1)</script> & \"tırnak\"", (await GetMessagesAsync(patient, conversation.Id)).Single().Text);
        Assert.Equal("<script>alert(1)</script> & \"tırnak\"", sent.Text);
    }

    [Fact]
    public async Task GetMessages_WithAfter_ReturnsOnlyNewer()
    {
        var (patientId, doctorId, conversation) = await OpenConversationAsync();
        var patient = PatientClient(patientId);
        var doctor = DoctorClient(NewId("du"), doctorId);
        var m1 = await SendAsync(patient, conversation.Id, "bir");
        await Task.Delay(20, Ct);
        var m2 = await SendAsync(doctor, conversation.Id, "iki");
        await Task.Delay(20, Ct);
        var m3 = await SendAsync(patient, conversation.Id, "üç");

        var afterFirst = await GetMessagesAsync(patient, conversation.Id, $"?after={Iso(m1.SentAt)}");
        var afterLast = await GetMessagesAsync(patient, conversation.Id, $"?after={Iso(m3.SentAt)}");

        Assert.Equal([m2.Id, m3.Id], afterFirst.Select(m => m.Id).ToArray()); // m1'in kendisi dahil değil (kesin büyük)
        Assert.Empty(afterLast);
    }

    [Fact]
    public async Task GetMessages_LimitKeepsLatest_AndRejectsOutOfRange()
    {
        var (patientId, _, conversation) = await OpenConversationAsync();
        var patient = PatientClient(patientId);
        var sent = new List<MessageDto>();
        for (var i = 0; i < 3; i++)
        {
            sent.Add(await SendAsync(patient, conversation.Id, $"mesaj {i}"));
            await Task.Delay(5, Ct);
        }

        var latestTwo = await GetMessagesAsync(patient, conversation.Id, "?limit=2");

        Assert.Equal([sent[1].Id, sent[2].Id], latestTwo.Select(m => m.Id).ToArray());
        foreach (var bad in new[] { 0, 101, -1 })
            Assert.Equal(HttpStatusCode.BadRequest, (await patient.GetAsync($"/api/chat/conversations/{conversation.Id}/messages?limit={bad}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Send_IncrementsOtherSidesUnread_AndReadResetsOnlyMine()
    {
        var (patientId, doctorId, conversation) = await OpenConversationAsync();
        var patient = PatientClient(patientId);
        var doctor = DoctorClient(NewId("du"), doctorId);

        await SendAsync(patient, conversation.Id, "1");
        await SendAsync(patient, conversation.Id, "2");
        await SendAsync(doctor, conversation.Id, "3");

        // Doktor 2 okunmamış, hasta 1 okunmamış; göndereninki artmaz.
        Assert.Equal(2, (await MineAsync(doctor, conversation.Id)).UnreadCount);
        Assert.Equal(1, (await MineAsync(patient, conversation.Id)).UnreadCount);
        Assert.Equal(2, await UnreadTotalAsync(doctor));
        Assert.Equal(1, await UnreadTotalAsync(patient));

        Assert.Equal(HttpStatusCode.NoContent, (await doctor.PostAsync($"/api/chat/conversations/{conversation.Id}/read", null, Ct)).StatusCode);

        Assert.Equal(0, (await MineAsync(doctor, conversation.Id)).UnreadCount);
        Assert.Equal(0, await UnreadTotalAsync(doctor));
        Assert.Equal(1, await UnreadTotalAsync(patient)); // hastanınki sıfırlanmadı
    }

    [Fact]
    public async Task ConcurrentSends_DoNotLoseUnreadIncrements()
    {
        var (patientId, doctorId, conversation) = await OpenConversationAsync();
        var patient = PatientClient(patientId);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => SendAsync(patient, conversation.Id, $"eşzamanlı {i}")));

        Assert.Equal(10, (await MineAsync(DoctorClient(NewId("du"), doctorId), conversation.Id)).UnreadCount); // atomik $inc
    }

    [Fact]
    public async Task UnreadTotal_SumsAcrossConversations()
    {
        var patientId = NewId("patient");
        var patient = PatientClient(patientId);
        var doctors = new[] { NewId("docA"), NewId("docB") };
        foreach (var d in doctors)
        {
            Factory.Downstream.AddDoctor(d, "Dr", d);
            Factory.Downstream.AddAppointment(patientId, d);
            var conv = (await (await patient.PostAsJsonAsync("/api/chat/conversations", new StartConversationDto { DoctorId = d }, Ct)).Content.ReadFromJsonAsync<ConversationDto>(Ct))!;
            await SendAsync(DoctorClient(NewId("du"), d), conv.Id, "selam");
            await SendAsync(DoctorClient(NewId("du"), d), conv.Id, "tekrar");
        }

        Assert.Equal(4, await UnreadTotalAsync(patient));
    }

    [Fact]
    public async Task Send_UpdatesConversationPreview_Truncated80_AndLastMessageAt()
    {
        var (patientId, _, conversation) = await OpenConversationAsync();
        var patient = PatientClient(patientId);
        var longText = string.Join(' ', Enumerable.Repeat("uzun", 40)); // 199 karakter

        var sent = await SendAsync(patient, conversation.Id, longText);

        var mine = await MineAsync(patient, conversation.Id);
        Assert.Equal(80, mine.LastMessagePreview!.Length);
        Assert.StartsWith("uzun uzun", mine.LastMessagePreview);
        Assert.Equal(sent.SentAt, mine.LastMessageAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Send_EmptyOrWhitespace_Returns400(string text)
    {
        var (patientId, _, conversation) = await OpenConversationAsync();

        var response = await PatientClient(patientId).PostAsJsonAsync($"/api/chat/conversations/{conversation.Id}/messages", new SendMessageDto { Text = text }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Send_TextLimit_2000Ok_2001Rejected()
    {
        var (patientId, _, conversation) = await OpenConversationAsync();
        var patient = PatientClient(patientId);

        Assert.Equal(HttpStatusCode.Created, (await patient.PostAsJsonAsync($"/api/chat/conversations/{conversation.Id}/messages", new SendMessageDto { Text = new string('a', 2000) }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await patient.PostAsJsonAsync($"/api/chat/conversations/{conversation.Id}/messages", new SendMessageDto { Text = new string('a', 2001) }, Ct)).StatusCode);
        // Kırpmadan sonra 2000 olan metin (baş/son boşluklu) kabul edilir.
        Assert.Equal(HttpStatusCode.Created, (await patient.PostAsJsonAsync($"/api/chat/conversations/{conversation.Id}/messages", new SendMessageDto { Text = " " + new string('b', 2000) + " " }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await patient.PostAsJsonAsync($"/api/chat/conversations/{conversation.Id}/messages", new SendMessageDto { Text = new string('c', 10_001) }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Send_BodyCannotSpoofSenderFields()
    {
        var (patientId, _, conversation) = await OpenConversationAsync("Gerçek Ad");
        var body = new StringContent("""{"text":"selam","senderRole":"Doctor","senderName":"Sahte","sentAt":"2000-01-01T00:00:00Z"}""", System.Text.Encoding.UTF8, "application/json");

        var response = await PatientClient(patientId, "Gerçek Ad").PostAsync($"/api/chat/conversations/{conversation.Id}/messages", body, Ct);

        var message = (await response.Content.ReadFromJsonAsync<MessageDto>(Ct))!;
        Assert.Equal("Patient", message.SenderRole);
        Assert.Equal("Gerçek Ad", message.SenderName);
        Assert.True(message.SentAt > new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task MessageDto_DoesNotLeakSenderId()
    {
        var (patientId, _, conversation) = await OpenConversationAsync();
        var patient = PatientClient(patientId);
        await SendAsync(patient, conversation.Id, "gizli kimlik testi");

        var json = await patient.GetStringAsync($"/api/chat/conversations/{conversation.Id}/messages", Ct);

        Assert.DoesNotContain("senderId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(patientId, json);
    }
}
