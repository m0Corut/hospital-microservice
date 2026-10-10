using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;

namespace Hastane.WebUI.Clients;

public interface IAdminPrescriptionClient
{
    Task<ApiResult<List<AdminPrescriptionListItemApiModel>>> GetAllAsync(string? patientId, string? doctorId, string? appointmentId, CancellationToken ct);

    Task<ApiResult<AdminPrescriptionApiModel>> GetAsync(Guid id, CancellationToken ct);

    // Reçete düzenleme/silme API'de DoctorOrAdmin: Admin her reçeteyi değiştirebilir. Kalemler eskilerin YERİNE geçer.
    Task<ApiResult> UpdateAsync(Guid id, string? notes, IReadOnlyList<DoctorPanelPrescriptionItemBody> items, CancellationToken ct);

    Task<ApiResult> DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class AdminPrescriptionClient(IGatewayClient gateway) : IAdminPrescriptionClient
{
    public Task<ApiResult<List<AdminPrescriptionListItemApiModel>>> GetAllAsync(string? patientId, string? doctorId, string? appointmentId, CancellationToken ct)
        => gateway.GetAsync<List<AdminPrescriptionListItemApiModel>>(
            AdminQuery.Build("/gateway/prescriptions",
                ("patientId", patientId), ("doctorId", doctorId), ("appointmentId", appointmentId)),
            ct: ct);

    public Task<ApiResult<AdminPrescriptionApiModel>> GetAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<AdminPrescriptionApiModel>($"/gateway/prescriptions/{id}", ct: ct);

    public Task<ApiResult> UpdateAsync(Guid id, string? notes, IReadOnlyList<DoctorPanelPrescriptionItemBody> items, CancellationToken ct)
        => gateway.PutAsync($"/gateway/prescriptions/{id}", new { id, notes, items }, ct: ct);

    public Task<ApiResult> DeleteAsync(Guid id, CancellationToken ct)
        => gateway.DeleteAsync($"/gateway/prescriptions/{id}", ct: ct);
}
