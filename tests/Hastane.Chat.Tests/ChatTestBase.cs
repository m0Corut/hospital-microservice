using System.Net;
using System.Net.Http.Json;
using Hastane.Chat.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Chat.Tests;

public abstract class ChatTestBase(ChatApiFactory factory)
{
    protected ChatApiFactory Factory { get; } = factory;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Her test kendi kimliklerini üretir: sınıf içindeki testler aynı veritabanını paylaşır, çakışmasın.
    protected static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    protected HttpClient Anonymous() => Factory.CreateClient();

    protected HttpClient PatientClient(string userId, string name = "Ayşe Hasta")
        => Factory.CreateClient().WithBearer(TestJwt.Create(userId, [HastaneRoles.Patient], name: name));

    protected HttpClient DoctorClient(string doctorUserId, string doctorId)
        => Factory.CreateClient().WithBearer(TestJwt.Create(doctorUserId, [HastaneRoles.Doctor], doctorId));

    protected HttpClient AdminClient()
        => Factory.CreateClient().WithBearer(TestJwt.Create("admin-1", [HastaneRoles.Admin]));

    // Doktor + randevulu hasta kurar, konuşmayı hasta açar.
    protected async Task<(string PatientId, string DoctorId, ConversationDto Conversation)> OpenConversationAsync(string patientName = "Ayşe Hasta")
    {
        var patientId = NewId("patient");
        var doctorId = NewId("doc");
        Factory.Downstream.AddDoctor(doctorId, "Mehmet", "Kaya", "Dr.");
        Factory.Downstream.AddAppointment(patientId, doctorId);

        var response = await PatientClient(patientId, patientName).PostAsJsonAsync("/api/chat/conversations", new StartConversationDto { DoctorId = doctorId }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (patientId, doctorId, (await response.Content.ReadFromJsonAsync<ConversationDto>(Ct))!);
    }

    protected async Task<MessageDto> SendAsync(HttpClient client, string conversationId, string text)
    {
        var response = await client.PostAsJsonAsync($"/api/chat/conversations/{conversationId}/messages", new SendMessageDto { Text = text }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MessageDto>(Ct))!;
    }

    protected async Task<List<MessageDto>> GetMessagesAsync(HttpClient client, string conversationId, string query = "")
    {
        var response = await client.GetAsync($"/api/chat/conversations/{conversationId}/messages{query}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<MessageDto>>(Ct))!;
    }

    protected async Task<int> UnreadTotalAsync(HttpClient client)
    {
        var dto = await client.GetFromJsonAsync<UnreadTotalDto>("/api/chat/unread", Ct);
        return dto!.Total;
    }

    protected async Task<ConversationDto> MineAsync(HttpClient client, string conversationId)
    {
        var list = await client.GetFromJsonAsync<List<ConversationDto>>("/api/chat/conversations", Ct);
        return list!.Single(c => c.Id == conversationId);
    }
}
