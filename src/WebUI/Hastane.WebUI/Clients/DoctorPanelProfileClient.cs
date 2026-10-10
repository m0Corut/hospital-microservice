using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.DoctorPanel;

namespace Hastane.WebUI.Clients;

public interface IDoctorPanelProfileClient
{
    Task<ApiResult<DoctorPanelDoctorApiModel>> GetAsync(string doctorId, CancellationToken ct);

    Task<ApiResult> UpdateAsync(string doctorId, DoctorPanelDoctorWriteBody body, CancellationToken ct);
}

public sealed class DoctorPanelProfileClient(IGatewayClient gateway) : IDoctorPanelProfileClient
{
    public Task<ApiResult<DoctorPanelDoctorApiModel>> GetAsync(string doctorId, CancellationToken ct)
        => gateway.GetAsync<DoctorPanelDoctorApiModel>($"/gateway/doctors/{Uri.EscapeDataString(doctorId)}", ct: ct);

    public Task<ApiResult> UpdateAsync(string doctorId, DoctorPanelDoctorWriteBody body, CancellationToken ct)
        => gateway.PutAsync($"/gateway/doctors/{Uri.EscapeDataString(doctorId)}", body, ct: ct);
}
