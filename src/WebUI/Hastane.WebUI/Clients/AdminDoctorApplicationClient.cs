using System.Globalization;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api;
using Hastane.WebUI.Models.Api.Admin;

namespace Hastane.WebUI.Clients;

// Doktor başvurularını yönetme (yalnız Admin). Onaydan sonra doktor kaydı ve kullanıcı bağı mevcut akışlarla ayrıca yapılır.
public interface IAdminDoctorApplicationClient
{
    // GET doctor-applications?status&page&pageSize -> { items, totalCount, page, pageSize }. Boş durum süzgeci yazılmaz.
    Task<ApiResult<AdminPagedApiModel<DoctorApplicationApiModel>>> ListAsync(string? status, int page, int pageSize, ApiFailures handle, CancellationToken ct);

    // Bekleyen başvuru sayısı (menü rozeti): pageSize=1 sorgusunun totalCount'u. Servis kapalıysa / yetki yoksa null.
    Task<long?> CountPendingAsync(CancellationToken ct);

    // POST doctor-applications/{id}/approve -> 200 | 404 | 409 (zaten karar verilmiş).
    Task<ApiResult<DoctorApplicationApiModel>> ApproveAsync(Guid id, CancellationToken ct);

    // POST doctor-applications/{id}/reject {note} -> 200 | 400 | 404 | 409.
    Task<ApiResult<DoctorApplicationApiModel>> RejectAsync(Guid id, string note, CancellationToken ct);
}

public sealed class AdminDoctorApplicationClient(IGatewayClient gateway) : IAdminDoctorApplicationClient
{
    public Task<ApiResult<AdminPagedApiModel<DoctorApplicationApiModel>>> ListAsync(string? status, int page, int pageSize, ApiFailures handle, CancellationToken ct)
    {
        var query = new List<string>
        {
            "page=" + page.ToString(CultureInfo.InvariantCulture),
            "pageSize=" + pageSize.ToString(CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrWhiteSpace(status)) query.Add("status=" + Uri.EscapeDataString(status.Trim()));
        return gateway.GetAsync<AdminPagedApiModel<DoctorApplicationApiModel>>("/gateway/doctor-applications?" + string.Join('&', query), handle, ct);
    }

    public async Task<long?> CountPendingAsync(CancellationToken ct)
    {
        try
        {
            var result = await ListAsync("Pending", 1, 1, ApiFailures.All, ct);
            return result.TryGetValue(out var page) ? page.TotalCount : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null; // rozet yardımcı bilgidir: bozuk yanıt sayfayı düşürmez
        }
    }

    public Task<ApiResult<DoctorApplicationApiModel>> ApproveAsync(Guid id, CancellationToken ct)
        => gateway.PostAsync<DoctorApplicationApiModel>($"/gateway/doctor-applications/{id}/approve", null, ApiFailures.NotFound, ct);

    public Task<ApiResult<DoctorApplicationApiModel>> RejectAsync(Guid id, string note, CancellationToken ct)
        => gateway.PostAsync<DoctorApplicationApiModel>($"/gateway/doctor-applications/{id}/reject", new { note }, ApiFailures.NotFound, ct);
}
