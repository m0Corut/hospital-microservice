using Hastane.Doctor.Api.Dtos;

namespace Hastane.Doctor.Api.Services;

public interface IDoctorService
{
    Task<List<ResultDoctorDto>> GetAllAsync(CancellationToken ct);
    Task<List<ResultDoctorDto>> GetByBranchIdAsync(string branchId, CancellationToken ct);
    Task<GetByIdDoctorDto?> GetByIdAsync(string id, CancellationToken ct);
    Task<ResultDoctorDto> CreateAsync(CreateDoctorDto dto, CancellationToken ct);
    /// <summary>Kayıt yoksa false döner.</summary>
    Task<bool> UpdateAsync(UpdateDoctorDto dto, CancellationToken ct);
    /// <summary>Kayıt yoksa false döner.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct);
}
