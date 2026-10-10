using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;

namespace Hastane.WebUI.Clients;

// Review servisi, Admin görünümü. API'de puan/metin süzgeci yok: yalnız doktora göre süzülür, gerisi ekranda yapılır.
public interface IAdminReviewClient
{
    // GET reviews[?doctorId=] (anonim uç; token varsa da çalışır).
    Task<ApiResult<List<AdminReviewApiModel>>> GetAllAsync(string? doctorId, CancellationToken ct);

    // DELETE reviews/{id}: Admin herkesin yorumunu silebilir (ReviewsController: Roles = Patient,Admin; Admin için sahiplik süzgeci yok).
    // 404 (zaten silinmiş) çağırana döner.
    Task<ApiResult> DeleteAsync(string id, CancellationToken ct);
}

public sealed class AdminReviewClient(IGatewayClient gateway) : IAdminReviewClient
{
    public Task<ApiResult<List<AdminReviewApiModel>>> GetAllAsync(string? doctorId, CancellationToken ct)
        => gateway.GetAsync<List<AdminReviewApiModel>>(AdminQuery.Build("/gateway/reviews", ("doctorId", doctorId)), ct: ct);

    public Task<ApiResult> DeleteAsync(string id, CancellationToken ct)
        => gateway.DeleteAsync($"/gateway/reviews/{Uri.EscapeDataString(id)}", ApiFailures.NotFound, ct);
}
