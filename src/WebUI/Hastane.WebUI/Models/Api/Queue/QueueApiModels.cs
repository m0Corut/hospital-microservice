namespace Hastane.WebUI.Models.Api.Queue;

// Queue servisinin kayıt yanıtı (hasta ve doktor ekranları). Status metin gelir: Waiting / Called / Completed / NoShow.
// AheadCount yalnız hastanın kendi ucunda (me/today) anlamlıdır.
public sealed class QueueEntryApiModel
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Number { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CalledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int AheadCount { get; set; }
}

// Herkese açık çağrı ekranı yanıtı: hasta/randevu kimliği yok, yalnız numara + maskeli ad.
public sealed class QueueDisplayApiModel
{
    public string DoctorId { get; set; } = string.Empty;
    public QueueDisplayItemApiModel? Current { get; set; }
    public List<QueueDisplayItemApiModel> Next { get; set; } = [];
}

public sealed class QueueDisplayItemApiModel
{
    public int Number { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? CalledAt { get; set; }
}

// Hasta kimliği gövdeden gitmez (token'dan); yalnız randevu.
public sealed record QueueCheckInRequest(Guid AppointmentId);
