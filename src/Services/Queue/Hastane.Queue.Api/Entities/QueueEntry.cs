namespace Hastane.Queue.Api.Entities;

// Bekleme salonundaki bir sıra kaydı. Randevu başına en çok bir kayıt (AppointmentId tekil).
public class QueueEntry
{
    public Guid Id { get; set; }

    public Guid AppointmentId { get; set; }

    public string DoctorId { get; set; } = string.Empty;

    // Sahiplik denetimi için (token sub). Hiçbir herkese açık çıktıda yer almaz.
    public string PatientUserId { get; set; } = string.Empty;

    // Gizlilik: "Can Y." gibi ad + soyad baş harfi. Tam ad hiçbir yerde SAKLANMAZ (bkz. DisplayNameMasker).
    public string DisplayName { get; set; } = string.Empty;

    // Doktor başına GÜNLÜK sıra numarası (QueueDate'e göre); tekil indeks (DoctorId, QueueDate, Number) çakışmayı engeller.
    public int Number { get; set; }

    // Kliniğin yerel günü (İstanbul). UTC günü değil: gece yarısı civarı numara sıfırlanması yerel güne göre olmalı.
    public DateOnly QueueDate { get; set; }

    public QueueStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CalledAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
