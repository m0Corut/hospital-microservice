using Hastane.Appointment.Api.Dtos;

namespace Hastane.Appointment.Api.Services;

public interface IAppointmentDetailService
{
    Task<AppointmentDetailDto?> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken ct);
    Task<OperationResult<AppointmentDetailDto>> UpsertAsync(Guid appointmentId, UpsertAppointmentDetailDto dto, CancellationToken ct);
}
