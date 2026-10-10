using System.ComponentModel.DataAnnotations;
using Hastane.Queue.Api.Entities;
using Hastane.Queue.Api.Validation;

namespace Hastane.Queue.Api.Dtos;

public class CheckInDto
{
    [NotEmptyGuid]
    public Guid AppointmentId { get; set; }
}

// Giriş yapmış kullanıcıya (hasta/doktor) dönen kayıt. PatientUserId bilerek YOK: doktor ekranına da hasta kimliği sızmaz.
public class QueueEntryDto
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Number { get; set; }
    public DateOnly QueueDate { get; set; }
    public QueueStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CalledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

// Hastanın kendi sırası + önündeki bekleyen sayısı (yalnız Waiting iken anlamlı, aksi halde 0).
public class MyQueueEntryDto : QueueEntryDto
{
    public int AheadCount { get; set; }
}

// Herkese açık biçim: yalnız bekleme salonunda zaten yüksek sesle söylenen/ekrana çıkan bilgi.
// Hasta kimliği ve randevu kimliği YOK.
public record QueueBroadcastDto(string DoctorId, int Number, string DisplayName, QueueStatus Status, DateTime? CalledAt);

public record DisplayItemDto(int Number, string DisplayName, QueueStatus Status, DateTime? CalledAt);

public class QueueDisplayDto
{
    public string DoctorId { get; set; } = string.Empty;
    public DateOnly QueueDate { get; set; }

    // Şu an çağrılan (yoksa null).
    public DisplayItemDto? Current { get; set; }

    // Sıradaki en çok 5 bekleyen (numara sırasıyla).
    public List<DisplayItemDto> Next { get; set; } = [];
}
