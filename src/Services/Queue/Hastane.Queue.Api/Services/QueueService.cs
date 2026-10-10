using Hastane.Queue.Api.Data;
using Hastane.Queue.Api.Dtos;
using Hastane.Queue.Api.Entities;
using Hastane.Queue.Api.Hubs;
using Hastane.Queue.Api.Integration;
using Hastane.Queue.Api.Settings;
using Hastane.Shared.Security;
using MapsterMapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hastane.Queue.Api.Services;

public class QueueService(
    QueueDbContext db, IMapper mapper, ICurrentUser user, IAppointmentLookup appointments,
    IOptions<SchedulingOptions> scheduling, TimeProvider time, IQueueNotifier notifier) : IQueueService
{
    // Eşzamanlı check-in'lerde "MAX+1" yarışını tekil indeks yakalar; yarışı kaybeden yeni numarayla yeniden dener.
    // 8 eşzamanlı istekte en kötü durumda sonuncu 8 denemede yer bulur; pay bırakıldı.
    private const int MaxNumberAttempts = 15;
    private const int DisplayNextCount = 5;

    private const string NotConfirmedMessage = "Yalnız onaylanmış randevu için sıraya girilebilir.";
    private const string NotTodayMessage = "Randevunuz bugün için değil; sıraya yalnız randevu gününde girilebilir.";
    private const string AppointmentUnavailableMessage = "Randevu servisine ulaşılamadı; randevu doğrulanamadı.";
    private const string AlreadyCalledMessage = "Bu doktorun çağrılmış bir hastası var; önce onu tamamlayın ya da 'gelmedi' işaretleyin.";

    // Ayardaki kimlik yanlışsa ilk kullanımda patlar; yanlış ayar sessizce yanlış gün üretmesin.
    private TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(scheduling.Value.TimeZone);

    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(UtcNow, Zone));

    public async Task<OperationResult<MyQueueEntryDto>> CheckInAsync(Guid appointmentId, string? fullName, CancellationToken ct)
    {
        var userId = user.UserId;
        if (userId is null) return OperationResult<MyQueueEntryDto>.NotFound();

        // İdempotent: aynı randevu zaten sırada ise (randevu sonradan Completed olsa bile) mevcut kaydı ver.
        var existing = await db.QueueEntries.AsNoTracking().FirstOrDefaultAsync(e => e.AppointmentId == appointmentId, ct);
        if (existing is not null) return await ExistingAsync(existing, userId, ct);

        // Sahiplik kararı Appointment servisinde: başkasının randevusu orada 404 döner, biz de 404 döneriz.
        var lookup = await appointments.GetAsync(appointmentId, ct);
        if (lookup.Outcome is LookupOutcome.NotFound or LookupOutcome.Forbidden) return OperationResult<MyQueueEntryDto>.NotFound();
        if (lookup.Outcome != LookupOutcome.Found) return OperationResult<MyQueueEntryDto>.ServiceUnavailable(AppointmentUnavailableMessage);

        var info = lookup.Value!;
        if (info.PatientId != userId) return OperationResult<MyQueueEntryDto>.NotFound(); // savunma: servis sızdırsa bile
        if (!string.Equals(info.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
            return OperationResult<MyQueueEntryDto>.Conflict(NotConfirmedMessage);

        var today = Today;
        var appointmentDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DisplayUtc(info.AppointmentDate), Zone));
        if (appointmentDay != today) return OperationResult<MyQueueEntryDto>.Conflict(NotTodayMessage);

        var masked = DisplayNameMasker.Mask(fullName);

        for (var attempt = 1; attempt <= MaxNumberAttempts; attempt++)
        {
            var max = await db.QueueEntries
                .Where(e => e.DoctorId == info.DoctorId && e.QueueDate == today)
                .MaxAsync(e => (int?)e.Number, ct);

            var entry = new QueueEntry
            {
                Id = Guid.CreateVersion7(),
                AppointmentId = appointmentId,
                DoctorId = info.DoctorId,
                PatientUserId = userId,
                DisplayName = masked,
                Number = (max ?? 0) + 1,
                QueueDate = today,
                Status = QueueStatus.Waiting,
                CreatedAt = UtcNow,
            };
            db.QueueEntries.Add(entry);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (IsUniqueViolation(ex))
            {
                db.ChangeTracker.Clear();

                // Aynı randevu için eşzamanlı ikinci istek kazandıysa yeni numara aramayız: onun kaydını döneriz.
                var winner = await db.QueueEntries.AsNoTracking().FirstOrDefaultAsync(e => e.AppointmentId == appointmentId, ct);
                if (winner is not null) return await ExistingAsync(winner, userId, ct);

                // Numara çakışması: kısa rastgele bekleme (aynı anda yeniden çarpışmasınlar) ve yeni MAX+1.
                await Task.Delay(Random.Shared.Next(5, 40), ct);
                continue;
            }

            await notifier.PublishAsync(entry, ct);
            return OperationResult<MyQueueEntryDto>.Created(await ToMineAsync(entry, ct));
        }

        return OperationResult<MyQueueEntryDto>.Conflict("Sıra numarası şu an alınamadı; lütfen yeniden deneyin.");
    }

    public async Task<List<MyQueueEntryDto>> GetMineTodayAsync(CancellationToken ct)
    {
        var userId = user.UserId;
        if (userId is null) return [];

        var today = Today;
        var entries = await db.QueueEntries.AsNoTracking()
            .Where(e => e.PatientUserId == userId && e.QueueDate == today)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        var result = new List<MyQueueEntryDto>(entries.Count);
        foreach (var entry in entries) result.Add(await ToMineAsync(entry, ct));
        return result;
    }

    public async Task<OperationResult<List<QueueEntryDto>>> GetDoctorTodayAsync(string? doctorId, CancellationToken ct)
    {
        // Doktor yalnız KENDİ sırasını görür (sorgudaki doctorId yok sayılır); Admin doctorId verir.
        var target = user.IsAdmin ? (string.IsNullOrWhiteSpace(doctorId) ? user.DoctorId : doctorId) : user.DoctorId;
        if (string.IsNullOrWhiteSpace(target))
        {
            return user.IsAdmin
                ? OperationResult<List<QueueEntryDto>>.Validation("doctorId zorunludur.")
                : OperationResult<List<QueueEntryDto>>.Forbidden("Hesabınız bir doktor kaydına bağlı değil.");
        }

        var today = Today;
        var entries = await db.QueueEntries.AsNoTracking()
            .Where(e => e.DoctorId == target && e.QueueDate == today)
            .OrderBy(e => e.Number)
            .ToListAsync(ct);
        return OperationResult<List<QueueEntryDto>>.Ok(mapper.Map<List<QueueEntryDto>>(entries));
    }

    public async Task<OperationResult<QueueEntryDto>> CallNextAsync(CancellationToken ct)
    {
        var doctorId = user.DoctorId;
        if (string.IsNullOrWhiteSpace(doctorId))
            return OperationResult<QueueEntryDto>.Forbidden("Hesabınız bir doktor kaydına bağlı değil.");

        var today = Today;
        var nextId = await db.QueueEntries.AsNoTracking()
            .Where(e => e.DoctorId == doctorId && e.QueueDate == today && e.Status == QueueStatus.Waiting)
            .OrderBy(e => e.Number)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);

        if (nextId is null) return OperationResult<QueueEntryDto>.Conflict("Sırada bekleyen hasta yok.");
        return await TransitionAsync(nextId.Value, QueueStatus.Called, ct);
    }

    public Task<OperationResult<QueueEntryDto>> CallAsync(Guid id, CancellationToken ct) => TransitionAsync(id, QueueStatus.Called, ct);

    public Task<OperationResult<QueueEntryDto>> CompleteAsync(Guid id, CancellationToken ct) => TransitionAsync(id, QueueStatus.Completed, ct);

    public Task<OperationResult<QueueEntryDto>> NoShowAsync(Guid id, CancellationToken ct) => TransitionAsync(id, QueueStatus.NoShow, ct);

    public async Task<QueueDisplayDto> GetDisplayAsync(string doctorId, CancellationToken ct)
    {
        var today = Today;
        var current = await db.QueueEntries.AsNoTracking()
            .Where(e => e.DoctorId == doctorId && e.QueueDate == today && e.Status == QueueStatus.Called)
            .OrderByDescending(e => e.CalledAt)
            .FirstOrDefaultAsync(ct);
        var next = await db.QueueEntries.AsNoTracking()
            .Where(e => e.DoctorId == doctorId && e.QueueDate == today && e.Status == QueueStatus.Waiting)
            .OrderBy(e => e.Number)
            .Take(DisplayNextCount)
            .ToListAsync(ct);

        return new QueueDisplayDto
        {
            DoctorId = doctorId,
            QueueDate = today,
            Current = current is null ? null : QueuePublic.ToItem(current),
            Next = next.Select(QueuePublic.ToItem).ToList(),
        };
    }

    // Durum geçişi KURALI (açık kural, otomatik tamamlama YOK):
    //   call:     Waiting -> Called. O doktorda zaten Called varsa 409 (öncekini sessizce Completed yapmayız;
    //             doktor önce Tamamla ya da Gelmedi der). Sıradaki olmak şart değil: doktor belirli birini çağırabilir.
    //   complete: Called -> Completed.   no-show: Waiting/Called -> NoShow.
    // Geçiş TEK koşullu UPDATE ile yapılır ("WHERE Status = eski"): iki eşzamanlı istekten yalnız biri satırı değiştirir.
    private async Task<OperationResult<QueueEntryDto>> TransitionAsync(Guid id, QueueStatus target, CancellationToken ct)
    {
        var entry = await db.QueueEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        // Başkasının kaydı ile olmayan kayıt aynı görünür (404): varlığı sızmaz.
        if (entry is null || !CanManage(entry.DoctorId)) return OperationResult<QueueEntryDto>.NotFound();

        if (entry.QueueDate != Today) return OperationResult<QueueEntryDto>.Conflict("Bu sıra kaydı bugüne ait değil.");

        var allowed = target switch
        {
            QueueStatus.Called => entry.Status == QueueStatus.Waiting,
            QueueStatus.Completed => entry.Status == QueueStatus.Called,
            QueueStatus.NoShow => entry.Status is QueueStatus.Waiting or QueueStatus.Called,
            _ => false,
        };
        if (!allowed) return OperationResult<QueueEntryDto>.Conflict($"{entry.Status} durumundaki kayıt {target} yapılamaz.");

        if (target == QueueStatus.Called)
        {
            var busy = await db.QueueEntries.AnyAsync(
                e => e.DoctorId == entry.DoctorId && e.QueueDate == entry.QueueDate && e.Status == QueueStatus.Called, ct);
            if (busy) return OperationResult<QueueEntryDto>.Conflict(AlreadyCalledMessage);
        }

        var now = (DateTime?)UtcNow;
        var scope = db.QueueEntries.Where(e => e.Id == id);
        int rows;
        try
        {
            rows = target switch
            {
                QueueStatus.Called => await scope.Where(e => e.Status == QueueStatus.Waiting)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, QueueStatus.Called).SetProperty(e => e.CalledAt, now), ct),
                QueueStatus.Completed => await scope.Where(e => e.Status == QueueStatus.Called)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, QueueStatus.Completed).SetProperty(e => e.CompletedAt, now), ct),
                _ => await scope.Where(e => e.Status == QueueStatus.Waiting || e.Status == QueueStatus.Called)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, QueueStatus.NoShow).SetProperty(e => e.CompletedAt, now), ct),
            };
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            // Filtreli tekil indeks: iki eşzamanlı çağrıdan kaybeden buraya düşer.
            return OperationResult<QueueEntryDto>.Conflict(AlreadyCalledMessage);
        }

        if (rows == 0) return OperationResult<QueueEntryDto>.Conflict("Kayıt başka bir işlemle değiştirildi; listeyi yenileyin.");

        var updated = await db.QueueEntries.AsNoTracking().FirstAsync(e => e.Id == id, ct);
        await notifier.PublishAsync(updated, ct);
        return OperationResult<QueueEntryDto>.Ok(mapper.Map<QueueEntryDto>(updated));
    }

    private bool CanManage(string entryDoctorId)
        => user.IsAdmin || (user.IsInRole(HastaneRoles.Doctor) && user.DoctorId is not null && user.DoctorId == entryDoctorId);

    private async Task<OperationResult<MyQueueEntryDto>> ExistingAsync(QueueEntry existing, string userId, CancellationToken ct)
        => existing.PatientUserId == userId
            ? OperationResult<MyQueueEntryDto>.Ok(await ToMineAsync(existing, ct))
            : OperationResult<MyQueueEntryDto>.NotFound();

    private async Task<MyQueueEntryDto> ToMineAsync(QueueEntry entry, CancellationToken ct)
    {
        var dto = mapper.Map<MyQueueEntryDto>(entry);
        if (entry.Status == QueueStatus.Waiting)
        {
            dto.AheadCount = await db.QueueEntries.AsNoTracking().CountAsync(
                e => e.DoctorId == entry.DoctorId && e.QueueDate == entry.QueueDate
                     && e.Status == QueueStatus.Waiting && e.Number < entry.Number, ct);
        }

        return dto;
    }

    private static DateTime DisplayUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();

    // 2601: benzersiz indeks, 2627: benzersiz kısıt ihlali.
    private static bool IsUniqueViolation(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqlException { Number: 2601 or 2627 }) return true;
        }

        return false;
    }
}
