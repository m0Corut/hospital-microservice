using Hastane.Queue.Api.Dtos;
using Hastane.Queue.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Hastane.Queue.Api.Hubs;

public static class QueueGroups
{
    public const string All = "all";
    public const string UpdatedMethod = "QueueUpdated";

    public static string Doctor(string doctorId) => "doctor:" + doctorId;
}

// MİMARİ NOT: Bu hub token İSTEMİYOR (anonim bağlanılır) ve bilerek yalnız DİNLEME sunar.
// Neden güvenli: hubdan yayınlanan tek şey bekleme salonu ekranında zaten herkese gösterilen kimliksiz bilgidir
// ({doctorId, number, displayName "Can Y.", status, calledAt}); hasta kimliği, randevu kimliği, tam ad yoktur.
// İstemci hub üzerinden hiçbir şey YAYINLAYAMAZ: aşağıdaki iki metot yalnız grup katılımıdır (veri döndürmez,
// kimseye mesaj göndermez). Yayını yalnız sunucu yapar (QueueNotifier), o da yalnız yetkili REST uçlarından sonra.
[AllowAnonymous]
public class QueueHub : Hub
{
    private const int MaxDoctorIdLength = 64;

    // Tek doktorun sırasını dinle (hastaya/doktora özel ekran).
    public Task JoinDoctor(string doctorId)
    {
        if (string.IsNullOrWhiteSpace(doctorId) || doctorId.Length > MaxDoctorIdLength)
            throw new HubException("Geçersiz doktor kimliği.");

        return Groups.AddToGroupAsync(Context.ConnectionId, QueueGroups.Doctor(doctorId));
    }

    // Tüm doktorların güncellemelerini dinle (genel çağrı ekranı).
    public Task JoinAll() => Groups.AddToGroupAsync(Context.ConnectionId, QueueGroups.All);
}

// Herkese açık yayın biçiminin TEK kurulduğu yer: bir alan eklenecekse burada, gözle görülür şekilde eklenir.
public static class QueuePublic
{
    public static QueueBroadcastDto ToBroadcast(QueueEntry e) => new(e.DoctorId, e.Number, e.DisplayName, e.Status, e.CalledAt);

    public static DisplayItemDto ToItem(QueueEntry e) => new(e.Number, e.DisplayName, e.Status, e.CalledAt);
}

public interface IQueueNotifier
{
    Task PublishAsync(QueueEntry entry, CancellationToken ct);
}

public sealed class QueueNotifier(IHubContext<QueueHub> hub, ILogger<QueueNotifier> logger) : IQueueNotifier
{
    public async Task PublishAsync(QueueEntry entry, CancellationToken ct)
    {
        try
        {
            // Doktor grubu + "all": bağlı istemci hangisine katıldıysa onu dinler.
            await hub.Clients.Groups(QueueGroups.Doctor(entry.DoctorId), QueueGroups.All)
                .SendAsync(QueueGroups.UpdatedMethod, QueuePublic.ToBroadcast(entry), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Yayın başarısızlığı sıra işlemini (zaten kaydedildi) geri almamalı; ekranlar 15 sn'lik yedek yenilemeyle toparlar.
            logger.LogWarning("Kuyruk güncellemesi yayınlanamadı: {ExceptionType}", ex.GetType().Name);
        }
    }
}
