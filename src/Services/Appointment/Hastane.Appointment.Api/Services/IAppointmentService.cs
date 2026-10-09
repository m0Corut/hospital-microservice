using Hastane.Appointment.Api.Dtos;
using Hastane.Appointment.Api.Entities;

namespace Hastane.Appointment.Api.Services;

public interface IAppointmentService
{
    Task<List<ResultAppointmentDto>> GetAllAsync(
        string? doctorId, string? patientId, AppointmentStatus? status, DateTime? from, DateTime? to, CancellationToken ct);
    Task<GetByIdAppointmentDto?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<OperationResult<ResultAppointmentDto>> CreateAsync(CreateAppointmentDto dto, CancellationToken ct);
    Task<OperationResult<ResultAppointmentDto>> RescheduleAsync(UpdateAppointmentDto dto, CancellationToken ct);
    Task<OperationResult<ResultAppointmentDto>> UpdateStatusAsync(Guid id, AppointmentStatus newStatus, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}
