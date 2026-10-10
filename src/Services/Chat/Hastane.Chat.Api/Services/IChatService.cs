using Hastane.Chat.Api.Dtos;

namespace Hastane.Chat.Api.Services;

// İsteği yapan: token'dan çıkarılan kimlik. Taraf (hasta/doktor) konuşma belgesiyle eşleşmeye göre serviste belirlenir.
public sealed record ChatCaller(string UserId, string? DoctorId, bool IsPatient, bool IsDoctor, string DisplayName);

public enum StartStatus { Created, Existing, NoAppointment, DoctorNotFound, Unavailable }

public sealed record StartResult(StartStatus Status, ConversationDto? Conversation = null);

public interface IChatService
{
    Task<StartResult> StartAsPatientAsync(ChatCaller caller, string doctorId, CancellationToken ct);
    Task<StartResult> StartAsDoctorAsync(ChatCaller caller, string patientUserId, CancellationToken ct);
    Task<List<ConversationDto>> ListAsync(ChatCaller caller, CancellationToken ct);

    // Konuşma yoksa ya da çağıran taraf değilse null (ikisi ayırt edilemez: varlığı sızdırmaz).
    Task<List<MessageDto>?> GetMessagesAsync(string conversationId, ChatCaller caller, DateTime? after, int limit, CancellationToken ct);
    Task<MessageDto?> SendAsync(string conversationId, ChatCaller caller, string text, CancellationToken ct);
    Task<bool> MarkReadAsync(string conversationId, ChatCaller caller, CancellationToken ct);
    Task<int> GetUnreadTotalAsync(ChatCaller caller, CancellationToken ct);
}
