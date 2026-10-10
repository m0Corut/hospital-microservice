using Hastane.Patient.Api.Dtos;

namespace Hastane.Patient.Api.Services;

public interface IPatientService
{
    Task<PatientProfileDto> GetMineAsync(CancellationToken ct);
    Task<PatientProfileDto> UpsertMineAsync(UpdatePatientProfileDto dto, CancellationToken ct);
    Task<OperationResult<PatientProfileDto>> GetByUserIdAsync(string userId, CancellationToken ct);
    Task<List<FavoriteDoctorDto>> GetFavoritesAsync(CancellationToken ct);
    Task<OperationResult<object>> AddFavoriteAsync(string doctorId, CancellationToken ct);
    Task RemoveFavoriteAsync(string doctorId, CancellationToken ct);
}
