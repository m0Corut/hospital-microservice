using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Patient;

namespace Hastane.WebUI.Clients;

// Hasta profili + favori doktorlar (Patient servisi). Kimlik her zaman token'dandır: "me" uçları.
public interface IPatientProfileClient
{
    Task<ApiResult<PatientProfileApiModel>> GetMineAsync(ApiFailures handle, CancellationToken ct);

    Task<ApiResult<PatientProfileApiModel>> UpdateMineAsync(UpdatePatientProfileRequest request, CancellationToken ct);

    // Sayfa başına TEK çağrı: hangi doktorların favori olduğu buradan bilinir (kart başına sorgu yok).
    Task<ApiResult<List<FavoriteDoctorApiModel>>> GetFavoritesAsync(ApiFailures handle, CancellationToken ct);

    Task<ApiResult> AddFavoriteAsync(string doctorId, ApiFailures handle, CancellationToken ct);

    Task<ApiResult> RemoveFavoriteAsync(string doctorId, ApiFailures handle, CancellationToken ct);
}

public sealed class PatientProfileClient(IGatewayClient gateway) : IPatientProfileClient
{
    public Task<ApiResult<PatientProfileApiModel>> GetMineAsync(ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<PatientProfileApiModel>("/gateway/patients/me", handle, ct);

    public Task<ApiResult<PatientProfileApiModel>> UpdateMineAsync(UpdatePatientProfileRequest request, CancellationToken ct)
        => gateway.PutAsync<PatientProfileApiModel>("/gateway/patients/me", request, ct: ct);

    public Task<ApiResult<List<FavoriteDoctorApiModel>>> GetFavoritesAsync(ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<List<FavoriteDoctorApiModel>>("/gateway/patients/me/favorites", handle, ct);

    public Task<ApiResult> AddFavoriteAsync(string doctorId, ApiFailures handle, CancellationToken ct)
        => gateway.PutAsync($"/gateway/patients/me/favorites/{Uri.EscapeDataString(doctorId)}", null, handle, ct);

    public Task<ApiResult> RemoveFavoriteAsync(string doctorId, ApiFailures handle, CancellationToken ct)
        => gateway.DeleteAsync($"/gateway/patients/me/favorites/{Uri.EscapeDataString(doctorId)}", handle, ct);
}
