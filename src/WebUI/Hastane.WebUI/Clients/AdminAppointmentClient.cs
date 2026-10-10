using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;

namespace Hastane.WebUI.Clients;

public interface IAdminAppointmentClient
{
    Task<ApiResult<List<AdminAppointmentApiModel>>> GetAllAsync(string? doctorId, string? patientId, string? status, string? from, string? to, CancellationToken ct);

    Task<ApiResult<AdminAppointmentApiModel>> GetAsync(Guid id, CancellationToken ct);

    // Durum değişikliği yalnız PATCH {id}/status; Admin her hedef durumu isteyebilir, geçiş kurallarını (409) API uygular.
    Task<ApiResult> SetStatusAsync(Guid id, string status, CancellationToken ct);

    Task<ApiResult> DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class AdminAppointmentClient(IGatewayClient gateway) : IAdminAppointmentClient
{
    public Task<ApiResult<List<AdminAppointmentApiModel>>> GetAllAsync(string? doctorId, string? patientId, string? status, string? from, string? to, CancellationToken ct)
        => gateway.GetAsync<List<AdminAppointmentApiModel>>(
            AdminQuery.Build("/gateway/appointments",
                ("doctorId", doctorId), ("patientId", patientId), ("status", status), ("from", from), ("to", to)),
            ct: ct);

    public Task<ApiResult<AdminAppointmentApiModel>> GetAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<AdminAppointmentApiModel>($"/gateway/appointments/{id}", ct: ct);

    public Task<ApiResult> DeleteAsync(Guid id, CancellationToken ct)
        => gateway.DeleteAsync($"/gateway/appointments/{id}", ct: ct);

    public Task<ApiResult> SetStatusAsync(Guid id, string status, CancellationToken ct)
        => gateway.PatchAsync($"/gateway/appointments/{id}/status", new { status }, ct: ct);
}
