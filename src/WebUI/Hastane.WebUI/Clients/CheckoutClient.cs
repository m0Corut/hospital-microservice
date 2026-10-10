using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Orders;

namespace Hastane.WebUI.Clients;

// Checkout ekranı: sepet özeti (Market) + sipariş verme (Order).
public interface ICheckoutClient
{
    Task<ApiResult<CheckoutCartApiModel>> GetCartAsync(ApiFailures handle, CancellationToken ct);

    // Idempotency-Key: aynı anahtar = aynı sipariş niyeti. Çift tıklama / yeniden gönderim ikinci sipariş açmaz.
    Task<ApiResult<OrderDetailApiModel>> PlaceOrderAsync(string idempotencyKey, CheckoutRequest request, ApiFailures handle, CancellationToken ct);
}

public sealed class CheckoutClient(IGatewayClient gateway) : ICheckoutClient
{
    public Task<ApiResult<CheckoutCartApiModel>> GetCartAsync(ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<CheckoutCartApiModel>("/gateway/cart", handle, ct);

    public Task<ApiResult<OrderDetailApiModel>> PlaceOrderAsync(string idempotencyKey, CheckoutRequest request, ApiFailures handle, CancellationToken ct)
        => gateway.PostAsync<OrderDetailApiModel>("/gateway/orders/checkout", request,
            new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey }, handle, ct);
}
