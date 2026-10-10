using Hastane.Contracts;
using Hastane.Queue.Api.Data;
using Hastane.Queue.Api.Entities;
using Hastane.Queue.Api.Hubs;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Queue.Api.Messaging;

// Randevu iptal edilince hâlâ BEKLEYEN sıra kaydı Cancelled olur ve SignalR "QueueUpdated" yayılır (ekran ve doktor listesi güncellenir).
// Yalnız Waiting kaydı iptal edilir: Called (içeride) ya da Completed/NoShow kayıtlara dokunulmaz.
// Idempotent: koşullu UPDATE ("WHERE Status = Waiting") ikinci kez 0 satır etkiler; kayıt Cancelled ise yayın yine yapılır
// (zararsız, olay yarıda kesilmişse ekranı toparlar).
public class AppointmentStatusChangedConsumer(QueueDbContext db, IQueueNotifier notifier, TimeProvider time)
    : IConsumer<AppointmentStatusChanged>
{
    public async Task Consume(ConsumeContext<AppointmentStatusChanged> context)
    {
        var message = context.Message;
        if (!string.Equals(message.NewStatus, "Cancelled", StringComparison.OrdinalIgnoreCase)) return;

        var ct = context.CancellationToken;
        var now = (DateTime?)time.GetUtcNow().UtcDateTime;
        await db.QueueEntries
            .Where(e => e.AppointmentId == message.AppointmentId && e.Status == QueueStatus.Waiting)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, QueueStatus.Cancelled).SetProperty(e => e.CompletedAt, now), ct);

        var entry = await db.QueueEntries.AsNoTracking().FirstOrDefaultAsync(e => e.AppointmentId == message.AppointmentId, ct);
        if (entry is { Status: QueueStatus.Cancelled }) await notifier.PublishAsync(entry, ct);
    }
}
