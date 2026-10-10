using System.Globalization;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Orders;

namespace Hastane.WebUI.Clients;

// Hastanın kendi siparişleri. Sahiplik API'dedir: başkasının siparişi 404 döner (ekranda 404 sayfası).
public interface IPatientOrderClient
{
    Task<ApiResult<OrderPagedApiModel<OrderListItemApiModel>>> ListAsync(int page, int pageSize, CancellationToken ct);

    Task<ApiResult<OrderDetailApiModel>> GetAsync(Guid id, CancellationToken ct);

    Task<ApiResult<OrderDetailApiModel>> CancelAsync(Guid id, string? reason, CancellationToken ct);
}

public sealed class PatientOrderClient(IGatewayClient gateway) : IPatientOrderClient
{
    public Task<ApiResult<OrderPagedApiModel<OrderListItemApiModel>>> ListAsync(int page, int pageSize, CancellationToken ct)
        => gateway.GetAsync<OrderPagedApiModel<OrderListItemApiModel>>(
            AdminQuery.Build("/gateway/orders",
                ("page", page.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture))),
            ct: ct);

    public Task<ApiResult<OrderDetailApiModel>> GetAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<OrderDetailApiModel>($"/gateway/orders/{id}", ct: ct);

    public Task<ApiResult<OrderDetailApiModel>> CancelAsync(Guid id, string? reason, CancellationToken ct)
        => gateway.PostAsync<OrderDetailApiModel>($"/gateway/orders/{id}/cancel", new CancelOrderRequest(reason), ct: ct);
}
