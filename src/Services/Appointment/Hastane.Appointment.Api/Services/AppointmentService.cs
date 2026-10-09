using Hastane.Appointment.Api.Data;
using Hastane.Appointment.Api.Dtos;
using Hastane.Appointment.Api.Entities;
using MapsterMapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using AppointmentEntity = Hastane.Appointment.Api.Entities.Appointment;

namespace Hastane.Appointment.Api.Services;

public class AppointmentService : IAppointmentService
{
    private readonly AppointmentDbContext _db;
    private readonly IMapper _mapper;

    // DbContext Scoped: istek başına bir tane. Değişiklik izleyicisi ve bağlantı o isteğe aittir, istekler arası paylaşılmaz.
    public AppointmentService(AppointmentDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    public async Task<List<ResultAppointmentDto>> GetAllAsync(
        string? doctorId, string? patientId, AppointmentStatus? status, DateTime? from, DateTime? to, CancellationToken ct)
    {
        // Sorgu burada yalnız tarif edilir; ToListAsync'e kadar SQL'e gitmez. İsteğe bağlı her filtre
        // gelmişse WHERE'e bir koşul daha ekler (hepsi AND ile birleşir), gelmemişse hiç dokunmaz.
        IQueryable<AppointmentEntity> query = _db.Appointments.AsNoTracking();

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
        return entity is null ? null : _mapper.Map<GetByIdAppointmentDto>(entity);
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

    public async Task<OperationResult<ResultAppointmentDto>> CreateAsync(CreateAppointmentDto dto, CancellationToken ct)
    {
        var entity = _mapper.Map<AppointmentEntity>(dto);
        // Sunucunun atadığı alanlar: istemci bunları gönderemez.
        entity.Id = Guid.CreateVersion7();
        entity.Status = AppointmentStatus.Pending;
        entity.CreatedAt = DateTime.UtcNow;
        entity.AppointmentDate = UtcDateTimeConverter.NormalizeToMinute(entity.AppointmentDate);

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
        if (entity is null) return OperationResult<ResultAppointmentDto>.NotFound();

        if (AppointmentStatusTransitions.IsFinal(entity.Status))
            return OperationResult<ResultAppointmentDto>.Conflict(
                $"{entity.Status} durumundaki randevu yeniden planlanamaz.");

        // Yalnız iki alan değişir; DoctorId, PatientId, Status, CreatedAt'e dokunulmaz.
        var slot = UtcDateTimeConverter.NormalizeToMinute(dto.AppointmentDate);
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
        if (entity is null) return OperationResult<ResultAppointmentDto>.NotFound();

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
