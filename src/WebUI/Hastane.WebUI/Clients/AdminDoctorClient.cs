using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;

namespace Hastane.WebUI.Clients;

public interface IAdminDoctorClient
{
    // Admin listesi/arama: GET /gateway/doctors/search (sayfalı).
    Task<ApiResult<AdminPagedApiModel<AdminDoctorListItemApiModel>>> SearchAsync(string? text, string? branchId, int page, int pageSize, CancellationToken ct);

    // Açılır liste ve ad çözme için tüm doktorlar: GET /gateway/doctors.
    Task<ApiResult<List<AdminDoctorListItemApiModel>>> GetAllAsync(ApiFailures handle, CancellationToken ct);

    Task<ApiResult<AdminDoctorApiModel>> GetAsync(string id, CancellationToken ct);

    Task<ApiResult<AdminDoctorListItemApiModel>> CreateAsync(AdminDoctorWriteBody body, CancellationToken ct);

    Task<ApiResult> UpdateAsync(string id, AdminDoctorUpdateBody body, CancellationToken ct);

    Task<ApiResult> DeleteAsync(string id, CancellationToken ct);
}

public sealed class AdminDoctorClient(IGatewayClient gateway) : IAdminDoctorClient
{
    public Task<ApiResult<AdminPagedApiModel<AdminDoctorListItemApiModel>>> SearchAsync(string? text, string? branchId, int page, int pageSize, CancellationToken ct)
        => gateway.GetAsync<AdminPagedApiModel<AdminDoctorListItemApiModel>>(
            AdminQuery.Build("/gateway/doctors/search",
                ("text", text), ("branchIds", branchId),
                ("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ct: ct);

    public Task<ApiResult<List<AdminDoctorListItemApiModel>>> GetAllAsync(ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<List<AdminDoctorListItemApiModel>>("/gateway/doctors", handle, ct);

    public Task<ApiResult<AdminDoctorApiModel>> GetAsync(string id, CancellationToken ct)
        => gateway.GetAsync<AdminDoctorApiModel>($"/gateway/doctors/{Uri.EscapeDataString(id)}", ct: ct);

    public Task<ApiResult<AdminDoctorListItemApiModel>> CreateAsync(AdminDoctorWriteBody body, CancellationToken ct)
        => gateway.PostAsync<AdminDoctorListItemApiModel>("/gateway/doctors", body, ct: ct);

    public Task<ApiResult> UpdateAsync(string id, AdminDoctorUpdateBody body, CancellationToken ct)
        => gateway.PutAsync($"/gateway/doctors/{Uri.EscapeDataString(id)}", body, ct: ct);

    public Task<ApiResult> DeleteAsync(string id, CancellationToken ct)
        => gateway.DeleteAsync($"/gateway/doctors/{Uri.EscapeDataString(id)}", ct: ct);
}
