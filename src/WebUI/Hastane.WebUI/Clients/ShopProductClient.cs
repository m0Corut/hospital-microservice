using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Market;
using Hastane.WebUI.Models.Api.Public;

namespace Hastane.WebUI.Clients;

// Herkese açık mağaza vitrini (anonim uçlar): ürün arama ve tek ürün.
public interface IShopProductClient
{
    Task<ApiResult<PagedApiModel<ProductApiModel>>> SearchAsync(string? search, string? category, int page, int pageSize, CancellationToken ct);

    // Ürün yoksa/pasifse API 404 verir; varsayılan davranış 404 sayfasıdır.
    Task<ApiResult<ProductApiModel>> GetAsync(Guid id, CancellationToken ct);
}

public sealed class ShopProductClient(IGatewayClient gateway) : IShopProductClient
{
    public Task<ApiResult<PagedApiModel<ProductApiModel>>> SearchAsync(string? search, string? category, int page, int pageSize, CancellationToken ct)
        => gateway.GetAsync<PagedApiModel<ProductApiModel>>(
            AdminQuery.Build("/gateway/products",
                ("search", search),
                ("category", category),
                ("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ct: ct);

    public Task<ApiResult<ProductApiModel>> GetAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<ProductApiModel>($"/gateway/products/{id}", ct: ct);
}
