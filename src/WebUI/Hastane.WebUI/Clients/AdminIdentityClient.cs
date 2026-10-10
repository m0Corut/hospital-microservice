using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;

namespace Hastane.WebUI.Clients;

// Identity servisinin Admin uçları: sayfalı kullanıcı listesi (arama + rol süzgeci), kullanıcı ayrıntısı, rol ve doktor bağı işlemleri.
public interface IAdminIdentityClient
{
    // GET users?page&pageSize&search&role (yalnız Admin) -> { items, totalCount, page, pageSize }. Boş süzgeç sorguya yazılmaz.
    Task<ApiResult<AdminPagedApiModel<AdminUserApiModel>>> ListUsersAsync(int page, int pageSize, string? search, string? role, CancellationToken ct);

    // DELETE users/{id}/doctor-link -> 204 (bağ yoksa da 204); kullanıcı yoksa 404.
    Task<ApiResult> UnlinkDoctorAsync(string userId, CancellationToken ct);

    Task<ApiResult<AdminUserApiModel>> GetUserAsync(string id, ApiFailures handle, CancellationToken ct);

    Task<ApiResult<List<AdminRoleApiModel>>> GetRolesAsync(CancellationToken ct) => GetRolesAsync(ApiFailures.None, ct);

    // Liste sayfasındaki rol süzgeci yardımcı bilgidir: roller alınamazsa sayfa yine açılır (handle = All).
    Task<ApiResult<List<AdminRoleApiModel>>> GetRolesAsync(ApiFailures handle, CancellationToken ct);

    Task<ApiResult<AdminRoleApiModel>> CreateRoleAsync(string name, CancellationToken ct);

    // Aşağıdaki üçü NotFound'u (kullanıcı/rol yok) çağırana döner: ekranda Detail mesajı gösterilir.
    Task<ApiResult<AdminUserApiModel>> AddRoleAsync(string userId, string role, CancellationToken ct);

    Task<ApiResult<AdminUserApiModel>> RemoveRoleAsync(string userId, string role, CancellationToken ct);

    Task<ApiResult<AdminUserApiModel>> LinkDoctorAsync(string userId, string doctorId, CancellationToken ct);
}

public sealed class AdminIdentityClient(IGatewayClient gateway) : IAdminIdentityClient
{
    public Task<ApiResult<AdminUserApiModel>> GetUserAsync(string id, ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<AdminUserApiModel>($"/gateway/users/{Uri.EscapeDataString(id)}", handle, ct);

    public Task<ApiResult<AdminPagedApiModel<AdminUserApiModel>>> ListUsersAsync(int page, int pageSize, string? search, string? role, CancellationToken ct)
    {
        var query = new List<string>
        {
            "page=" + page.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "pageSize=" + pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrWhiteSpace(search)) query.Add("search=" + Uri.EscapeDataString(search.Trim()));
        if (!string.IsNullOrWhiteSpace(role)) query.Add("role=" + Uri.EscapeDataString(role.Trim()));
        return gateway.GetAsync<AdminPagedApiModel<AdminUserApiModel>>("/gateway/users?" + string.Join('&', query), ct: ct);
    }

    public Task<ApiResult> UnlinkDoctorAsync(string userId, CancellationToken ct)
        => gateway.DeleteAsync($"/gateway/users/{Uri.EscapeDataString(userId)}/doctor-link", ApiFailures.NotFound, ct);

    public Task<ApiResult<List<AdminRoleApiModel>>> GetRolesAsync(ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<List<AdminRoleApiModel>>("/gateway/roles", handle, ct);

    public Task<ApiResult<AdminRoleApiModel>> CreateRoleAsync(string name, CancellationToken ct)
        => gateway.PostAsync<AdminRoleApiModel>("/gateway/roles", new { name }, ct: ct);

    public Task<ApiResult<AdminUserApiModel>> AddRoleAsync(string userId, string role, CancellationToken ct)
        => gateway.PostAsync<AdminUserApiModel>($"/gateway/users/{Uri.EscapeDataString(userId)}/roles", new { role }, ApiFailures.NotFound, ct);

    public async Task<ApiResult<AdminUserApiModel>> RemoveRoleAsync(string userId, string role, CancellationToken ct)
    {
        // IGatewayClient.DeleteAsync gövdesiz (Unit) döner; rol çıkarma uç yanıtı kullanıcı özeti olsa da ekran sayfayı yeniden yükler.
        var result = await gateway.DeleteAsync($"/gateway/users/{Uri.EscapeDataString(userId)}/roles/{Uri.EscapeDataString(role)}", ApiFailures.NotFound, ct);
        return new ApiResult<AdminUserApiModel> { Kind = result.Kind, StatusCode = result.StatusCode, Problem = result.Problem };
    }

    public Task<ApiResult<AdminUserApiModel>> LinkDoctorAsync(string userId, string doctorId, CancellationToken ct)
        => gateway.PutAsync<AdminUserApiModel>($"/gateway/users/{Uri.EscapeDataString(userId)}/doctor-link", new { doctorId }, ApiFailures.NotFound, ct);
}
