namespace Hastane.WebUI.Models.Api.Chat;

// Chat servisinin DTO'larının WebUI kopyası. Mesaj metni HAM gelir; HTML kodlama görünümün işidir.
public sealed class ConversationApiModel
{
    public string Id { get; set; } = string.Empty;
    public string PatientUserId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    // İsteği yapanın tarafı için okunmamış sayısı.
    public int UnreadCount { get; set; }
}

public sealed class MessageApiModel
{
    public string Id { get; set; } = string.Empty;
    // "Patient" ya da "Doctor".
    public string SenderRole { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}

public sealed class UnreadTotalApiModel
{
    public int Total { get; set; }
}

public sealed record StartConversationRequest(string DoctorId);

public sealed record StartConversationWithPatientRequest(string PatientUserId);

public sealed record SendMessageRequest(string Text);
