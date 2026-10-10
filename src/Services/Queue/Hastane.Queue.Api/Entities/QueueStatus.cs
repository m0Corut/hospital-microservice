namespace Hastane.Queue.Api.Entities;

// Veritabanında metin saklanır (bkz. QueueEntryConfiguration).
public enum QueueStatus
{
    Waiting,
    Called,
    Completed,
    NoShow,
}
