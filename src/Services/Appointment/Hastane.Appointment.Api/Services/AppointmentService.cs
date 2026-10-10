using Hastane.Appointment.Api.Data;
using Hastane.Appointment.Api.Dtos;
using Hastane.Appointment.Api.Entities;
using Hastane.Appointment.Api.Integration;
using Hastane.Appointment.Api.Settings;
using Hastane.Shared.Security;
using MapsterMapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AppointmentEntity = Hastane.Appointment.Api.Entities.Appointment;

namespace Hastane.Appointment.Api.Services;

public class AppointmentService : IAppointmentService
{
    private readonly AppointmentDbContext _db;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _user;
    private readonly IDoctorScheduleLookup _schedules;
    private readonly IOptions<SchedulingOptions> _scheduling;

    // DbContext Scoped: istek başına bir tane. Değişiklik izleyicisi ve bağlantı o isteğe aittir, istekler arası paylaşılmaz.
    public AppointmentService(
        AppointmentDbContext db, IMapper mapper, ICurrentUser user,
        IDoctorScheduleLookup schedules, IOptions<SchedulingOptions> scheduling)
    {
        _db = db;
        _mapper = mapper;
        _user = user;
        _schedules = schedules;
        _scheduling = scheduling;
    }

    private const string NotInScheduleMessage = "Seçilen saat doktorun çalışma programında değil";
    private const string DoctorUnavailableMessage = "Doktor servisine ulaşılamadı; doktorun programı doğrulanamadı.";

    // Ayardaki kimlik yanlışsa ilk kullanımda patlar; yanlış ayar sessizce yanlış saat üretmesin.
    private TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(_scheduling.Value.TimeZone);

    public async Task<OperationResult<List<AvailableSlotDto>>> GetAvailabilityAsync(
        string doctorId, DateOnly date, CancellationToken ct)
    {
        var lookup = await _schedules.GetScheduleAsync(doctorId, ct);
        if (lookup.Outcome == LookupOutcome.NotFound) return OperationResult<List<AvailableSlotDto>>.NotFound();
        if (lookup.Outcome != LookupOutcome.Found) return OperationResult<List<AvailableSlotDto>>.Unavailable(DoctorUnavailableMessage);

        var slots = SlotCalculator.SlotsForLocalDate(lookup.Value!, date, Zone);
        if (slots.Count == 0) return OperationResult<List<AvailableSlotDto>>.Ok(slots);

        // Dolu saatler: o doktorun iptal edilmemiş randevuları (slot kuralıyla aynı: Cancelled işgal etmez).
        var first = slots[0].Start;
        var last = slots[^1].Start;
        var taken = (await _db.Appointments.AsNoTracking()
                .Where(a => a.DoctorId == doctorId && a.Status != AppointmentStatus.Cancelled
                            && a.AppointmentDate >= first && a.AppointmentDate <= last)
                .Select(a => a.AppointmentDate)
                .ToListAsync(ct))
            .Select(UtcDateTimeConverter.NormalizeToMinute)
            .ToHashSet();

        var now = DateTime.UtcNow;
        return OperationResult<List<AvailableSlotDto>>.Ok(slots.Where(s => s.Start > now && !taken.Contains(s.Start)).ToList());
    }

    public async Task<List<ResultAppointmentDto>> GetAllAsync(
        string? doctorId, string? patientId, AppointmentStatus? status, DateTime? from, DateTime? to, CancellationToken ct)
    {
        // Sorgu burada yalnız tarif edilir; ToListAsync'e kadar SQL'e gitmez. İsteğe bağlı her filtre
        // gelmişse WHERE'e bir koşul daha ekler (hepsi AND ile birleşir), gelmemişse hiç dokunmaz.
        IQueryable<AppointmentEntity> query = _db.Appointments.AsNoTracking();

        // Liste filtresi ZORLAMA (IDOR savunması): Admin dışında kapsamı istemci değil token belirler.
        // Hasta ?patientId=başkası yazsa bile değer token'daki sub ile EZİLİR; doktor için doctorId aynı şekilde.
        if (!_user.IsAdmin)
        {
            var isPatient = _user.IsInRole(HastaneRoles.Patient) && _user.UserId is not null;
            var isDoctor = _user.IsInRole(HastaneRoles.Doctor) && _user.DoctorId is not null;
            var me = _user.UserId;
            var myDoctor = _user.DoctorId;

            if (isPatient && isDoctor)
                query = query.Where(a => a.PatientId == me || a.DoctorId == myDoctor);
            else if (isPatient)
                patientId = me;
            else if (isDoctor)
                doctorId = myDoctor;
            else
                return []; // doctor_id'si olmayan Doctor ya da rolsüz kullanıcı: görecek bir şey yok
        }

        if (!string.IsNullOrWhiteSpace(doctorId)) query = query.Where(a => a.DoctorId == doctorId);
        if (!string.IsNullOrWhiteSpace(patientId)) query = query.Where(a => a.PatientId == patientId);
        if (status is not null) query = query.Where(a => a.Status == status);
        if (from is not null)
        {
            var fromUtc = UtcDateTimeConverter.Normalize(from.Value);
            query = query.Where(a => a.AppointmentDate >= fromUtc);
        }
        if (to is not null)
        {
            var toUtc = UtcDateTimeConverter.Normalize(to.Value);
            query = query.Where(a => a.AppointmentDate <= toUtc);
        }

        var list = await query.OrderBy(a => a.AppointmentDate).ThenBy(a => a.Id).ToListAsync(ct);
        return _mapper.Map<List<ResultAppointmentDto>>(list);
    }

    public async Task<GetByIdAppointmentDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        // Include: randevu ile detayı tek sorguda (LEFT JOIN) getirir; detay yoksa Detail null kalır.
        var entity = await _db.Appointments.AsNoTracking()
            .Include(a => a.Detail)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        // Sahibi olmayan için "yok" ile aynı cevap (null -> 404): varlığını sızdırmayız.
        return entity is null || !AppointmentAccess.CanAccess(_user, entity) ? null : _mapper.Map<GetByIdAppointmentDto>(entity);
    }

    private const string SlotTakenMessage = "Bu doktorun bu saatte başka bir randevusu var.";

    // SQL Server: 2601 = tekil indeks ihlali, 2627 = tekil kısıt ihlali.
    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is SqlException { Number: 2601 or 2627 };

    // Cancelled slotu işgal etmez. excludeId: yeniden planlamada randevunun kendisi sahte çakışma vermesin.
    private async Task<bool> IsSlotTakenAsync(string doctorId, DateTime slot, Guid? excludeId, CancellationToken ct)
    {
        var query = _db.Appointments.Where(a =>
            a.DoctorId == doctorId && a.AppointmentDate == slot && a.Status != AppointmentStatus.Cancelled);
        if (excludeId is { } self) query = query.Where(a => a.Id != self);
        return await query.AnyAsync(ct);
    }

    // POST ve PUT aynı kuralı paylaşır: null = saat programda; aksi halde hazır hata sonucu (400 / 503).
    private async Task<OperationResult<T>?> CheckScheduleAsync<T>(string doctorId, DateTime slot, CancellationToken ct)
    {
        var lookup = await _schedules.GetScheduleAsync(doctorId, ct);
        if (lookup.Outcome == LookupOutcome.NotFound) return OperationResult<T>.Invalid("Doktor bulunamadı.");
        if (lookup.Outcome != LookupOutcome.Found) return OperationResult<T>.Unavailable(DoctorUnavailableMessage);
        var zone = Zone;
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(slot, zone));
        return SlotCalculator.SlotsForLocalDate(lookup.Value!, localDate, zone).Any(sl => sl.Start == slot)
            ? null
            : OperationResult<T>.Invalid(NotInScheduleMessage);
    }

    public async Task<OperationResult<ResultAppointmentDto>> CreateAsync(CreateAppointmentDto dto, CancellationToken ct)
    {
        var entity = _mapper.Map<AppointmentEntity>(dto);
        // Kimlik token'dan: Admin dışında gövdedeki PatientId YOK SAYILIR. Admin başkası adına açabilir (controller boş olmadığını denetler).
        if (_user.IsAdmin)
            entity.PatientId = dto.PatientId!;
        else if (_user.UserId is { } sub)
            entity.PatientId = sub;
        else
            return OperationResult<ResultAppointmentDto>.Forbidden();
        // Sunucunun atadığı alanlar: istemci bunları gönderemez.
        entity.Id = Guid.CreateVersion7();
        entity.Status = AppointmentStatus.Pending;
        entity.CreatedAt = DateTime.UtcNow;
        entity.AppointmentDate = UtcDateTimeConverter.NormalizeToMinute(entity.AppointmentDate);

        entity.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();

        // Program kontrolü (Admin dahil herkes): saat doktorun haftalık programındaki bir slot başlangıcı olmalı.
        // Doctor servisi cevap vermezse randevu AÇILMAZ (503): doğrulanamayan saat kabul edilmez.
        var scheduleError = await CheckScheduleAsync<ResultAppointmentDto>(entity.DoctorId, entity.AppointmentDate, ct);
        if (scheduleError is not null) return scheduleError;

        // Katman 1: güzel hata mesajı için önce bak.
        if (await IsSlotTakenAsync(entity.DoctorId, entity.AppointmentDate, null, ct))
            return OperationResult<ResultAppointmentDto>.Conflict(SlotTakenMessage);

        _db.Appointments.Add(entity);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Katman 2: iki istek kontrolü aynı anda geçtiyse tekil indeks ikincisini reddetti.
            return OperationResult<ResultAppointmentDto>.Conflict(SlotTakenMessage);
        }
        return OperationResult<ResultAppointmentDto>.Created(_mapper.Map<ResultAppointmentDto>(entity));
    }

    public async Task<OperationResult<ResultAppointmentDto>> RescheduleAsync(UpdateAppointmentDto dto, CancellationToken ct)
    {
        var entity = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == dto.Id, ct);
        if (entity is null || !AppointmentAccess.CanAccess(_user, entity)) return OperationResult<ResultAppointmentDto>.NotFound();

        if (AppointmentStatusTransitions.IsFinal(entity.Status))
            return OperationResult<ResultAppointmentDto>.Conflict(
                $"{entity.Status} durumundaki randevu yeniden planlanamaz.");

        // Yalnız iki alan değişir; DoctorId, PatientId, Status, CreatedAt'e dokunulmaz.
        var slot = UtcDateTimeConverter.NormalizeToMinute(dto.AppointmentDate);
        var scheduleError = await CheckScheduleAsync<ResultAppointmentDto>(entity.DoctorId, slot, ct);
        if (scheduleError is not null) return scheduleError;
        if (await IsSlotTakenAsync(entity.DoctorId, slot, entity.Id, ct))
            return OperationResult<ResultAppointmentDto>.Conflict(SlotTakenMessage);

        entity.BranchId = dto.BranchId;
        entity.AppointmentDate = slot;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return OperationResult<ResultAppointmentDto>.Conflict(SlotTakenMessage);
        }
        return OperationResult<ResultAppointmentDto>.Ok(_mapper.Map<ResultAppointmentDto>(entity));
    }

    public async Task<OperationResult<ResultAppointmentDto>> UpdateStatusAsync(
        Guid id, AppointmentStatus newStatus, CancellationToken ct)
    {
        var entity = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (entity is null || !AppointmentAccess.CanAccess(_user, entity)) return OperationResult<ResultAppointmentDto>.NotFound();

        // Sıra: önce sahiplik (404), sonra alan düzeyinde yetki (403), en son durum makinesi (409).
        if (!AppointmentAccess.CanSetStatus(_user, entity, newStatus)) return OperationResult<ResultAppointmentDto>.Forbidden();

        if (!AppointmentStatusTransitions.CanTransition(entity.Status, newStatus))
            return OperationResult<ResultAppointmentDto>.Conflict(
                $"{entity.Status} durumundan {newStatus} durumuna geçilemez.");

        entity.Status = newStatus;
        await _db.SaveChangesAsync(ct);
        return OperationResult<ResultAppointmentDto>.Ok(_mapper.Map<ResultAppointmentDto>(entity));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        // Tek DELETE komutu; detay satırını veritabanındaki ON DELETE CASCADE siler.
        var deleted = await _db.Appointments.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
        return deleted > 0;
    }
}
