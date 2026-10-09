using Hastane.Appointment.Api.Data;
using Hastane.Appointment.Api.Dtos;
using Hastane.Appointment.Api.Entities;
using Hastane.Shared.Security;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Appointment.Api.Services;

public class AppointmentDetailService : IAppointmentDetailService
{
    private readonly AppointmentDbContext _db;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _user;

    public AppointmentDetailService(AppointmentDbContext db, IMapper mapper, ICurrentUser user)
    {
        _db = db;
        _mapper = mapper;
        _user = user;
    }

    public async Task<AppointmentDetailDto?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken ct)
    {
        // Detay, randevunun sahibine aittir: önce randevuya bakılır; görmeye hakkı yoksa "detay yok" gibi 404.
        var appointment = await _db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (appointment is null || !AppointmentAccess.CanAccess(_user, appointment)) return null;

        var detail = await _db.AppointmentDetails.AsNoTracking()
            .FirstOrDefaultAsync(d => d.AppointmentId == appointmentId, ct);
        return detail is null ? null : _mapper.Map<AppointmentDetailDto>(detail);
    }

    public async Task<OperationResult<AppointmentDetailDto>> UpsertAsync(
        Guid appointmentId, UpsertAppointmentDetailDto dto, CancellationToken ct)
    {
        // Yazma: yalnız randevunun doktoru ya da Admin (hasta yazamaz; rol politikası zaten 403 verir).
        var appointment = await _db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (appointment is null || !(_user.IsAdmin || AppointmentAccess.IsDoctorOf(_user, appointment)))
            return OperationResult<AppointmentDetailDto>.NotFound();

        var detail = await _db.AppointmentDetails.FirstOrDefaultAsync(d => d.AppointmentId == appointmentId, ct);
        var isNew = detail is null;

        if (detail is null)
        {
            detail = _mapper.Map<AppointmentDetail>(dto);
            detail.Id = Guid.CreateVersion7();
            detail.AppointmentId = appointmentId;
            _db.AppointmentDetails.Add(detail);
        }
        else
        {
            detail.Complaint = dto.Complaint;
            detail.Diagnosis = dto.Diagnosis;
            detail.DoctorNotes = dto.DoctorNotes;
            detail.UpdatedAt = DateTime.UtcNow;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (isNew)
        {
            // İki istek aynı anda ilk detayı eklemeye kalkarsa tekil indeks ikincisini reddeder; 500 yerine 409.
            return OperationResult<AppointmentDetailDto>.Conflict("Bu randevu için detay aynı anda başka bir istekle oluşturuldu, tekrar deneyin.");
        }

        var result = _mapper.Map<AppointmentDetailDto>(detail);
        return isNew ? OperationResult<AppointmentDetailDto>.Created(result) : OperationResult<AppointmentDetailDto>.Ok(result);
    }
}
