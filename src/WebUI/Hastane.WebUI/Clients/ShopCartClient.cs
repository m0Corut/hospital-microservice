using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Market;

namespace Hastane.WebUI.Clients;

// Hastanın sepeti (Market servisi, Redis). Yalnız Patient rolü çağırır.
public interface IShopCartClient
{
    Task<ApiResult<CartApiModel>> GetAsync(ApiFailures handle, CancellationToken ct);

    // Adedi MUTLAK değere ayarlar (0 = sil). Stok üstü 409, geçersiz adet 400, ürün satışta değil 404.
    Task<ApiResult<CartApiModel>> SetItemAsync(Guid productId, int quantity, ApiFailures handle, CancellationToken ct);

    Task<ApiResult> ClearAsync(CancellationToken ct);
}

public sealed class ShopCartClient(IGatewayClient gateway) : IShopCartClient
{
    public Task<ApiResult<CartApiModel>> GetAsync(ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<CartApiModel>("/gateway/cart", handle, ct);

    public Task<ApiResult<CartApiModel>> SetItemAsync(Guid productId, int quantity, ApiFailures handle, CancellationToken ct)
        => gateway.PutAsync<CartApiModel>($"/gateway/cart/items/{productId}", new SetCartItemBody { Quantity = quantity }, handle, ct);

    public Task<ApiResult> ClearAsync(CancellationToken ct)
        => gateway.DeleteAsync("/gateway/cart", ct: ct);
}
