using System.Globalization;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Orders;

namespace Hastane.WebUI.Clients;

// Yönetici sipariş ekranları. Durum makinesi (geçersiz geçiş 409) ve iptal kuralları API'dedir.
public interface IAdminOrderClient
{
    Task<ApiResult<OrderPagedApiModel<OrderListItemApiModel>>> ListAsync(string? status, int page, int pageSize, CancellationToken ct);

    Task<ApiResult<OrderDetailApiModel>> GetAsync(Guid id, CancellationToken ct);

    Task<ApiResult> UpdateStatusAsync(Guid id, string status, CancellationToken ct);

    Task<ApiResult<OrderDetailApiModel>> CancelAsync(Guid id, string? reason, CancellationToken ct);
}

public sealed class AdminOrderClient(IGatewayClient gateway) : IAdminOrderClient
{
    public Task<ApiResult<OrderPagedApiModel<OrderListItemApiModel>>> ListAsync(string? status, int page, int pageSize, CancellationToken ct)
        => gateway.GetAsync<OrderPagedApiModel<OrderListItemApiModel>>(
            AdminQuery.Build("/gateway/orders",
                ("status", status),
                ("page", page.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture))),
            ct: ct);

    public Task<ApiResult<OrderDetailApiModel>> GetAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<OrderDetailApiModel>($"/gateway/orders/{id}", ct: ct);

    // Yanıt gövdesi gerekmez: başarıda ekran siparişin ayrıntı sayfasına yönlenir ve güncel hali oradan okunur.
    public Task<ApiResult> UpdateStatusAsync(Guid id, string status, CancellationToken ct)
        => gateway.PatchAsync($"/gateway/orders/{id}/status", new UpdateOrderStatusRequest(status), ct: ct);

    public Task<ApiResult<OrderDetailApiModel>> CancelAsync(Guid id, string? reason, CancellationToken ct)
        => gateway.PostAsync<OrderDetailApiModel>($"/gateway/orders/{id}/cancel", new CancelOrderRequest(reason), ct: ct);
}
