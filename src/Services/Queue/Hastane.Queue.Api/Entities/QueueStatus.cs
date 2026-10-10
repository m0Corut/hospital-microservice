namespace Hastane.Queue.Api.Entities;

// Veritabanında metin saklanır (bkz. QueueEntryConfiguration).
public enum QueueStatus
{
    Waiting,
    Called,
    Completed,
    NoShow,

    // Randevu iptal edildiği için bekleyen sıra kaydı kapatıldı (AppointmentStatusChanged olayı, bkz. Messaging).
    Cancelled,
}
