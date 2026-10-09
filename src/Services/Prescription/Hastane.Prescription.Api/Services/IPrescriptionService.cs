using Hastane.Prescription.Api.Dtos;

namespace Hastane.Prescription.Api.Services;

public interface IPrescriptionService
{
    Task<List<ResultPrescriptionDto>> GetAllAsync(
        string? patientId, string? doctorId, Guid? appointmentId, CancellationToken ct);
    Task<GetByIdPrescriptionDto?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<OperationResult<GetByIdPrescriptionDto>> CreateAsync(CreatePrescriptionDto dto, CancellationToken ct);
    Task<OperationResult<GetByIdPrescriptionDto>> UpdateAsync(UpdatePrescriptionDto dto, CancellationToken ct);
    // Ok / NotFound / Forbidden
    Task<OperationStatus> DeleteAsync(Guid id, CancellationToken ct);
}
