using System.ComponentModel.DataAnnotations;

namespace Hastane.Chat.Api.Dtos;

public class StartConversationDto
{
    [Required, MaxLength(64)]
    public string DoctorId { get; set; } = string.Empty;
}

public class StartConversationWithPatientDto
{
    [Required, MaxLength(64)]
    public string PatientUserId { get; set; } = string.Empty;
}

public class SendMessageDto
{
    // 1-2000 karakter KIRPILMIŞ metin üzerinden denetlenir (controller); buradaki üst sınır yalnız aşırı büyük gövdeye karşı.
    [Required, MaxLength(10_000)]
    public string Text { get; set; } = string.Empty;
}

public class ConversationDto
{
    public string Id { get; set; } = string.Empty;
    public string PatientUserId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }

    // İsteği yapanın TARAFI için okunmamış sayısı (hasta ise PatientUnread, doktor ise DoctorUnread).
    public int UnreadCount { get; set; }
}

public class MessageDto
{
    public string Id { get; set; } = string.Empty;
    public string SenderRole { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}

public class UnreadTotalDto
{
    public int Total { get; set; }
}
