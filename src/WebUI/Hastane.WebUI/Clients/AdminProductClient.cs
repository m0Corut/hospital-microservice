using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Market;
using Hastane.WebUI.Models.Api.Public;

namespace Hastane.WebUI.Clients;

// Yönetici ürün yönetimi. Liste pasif ürünleri de getirir (includeInactive yalnız Admin token'ıyla etkilidir).
public interface IAdminProductClient
{
    Task<ApiResult<PagedApiModel<ProductApiModel>>> ListAsync(string? search, string? category, int page, int pageSize, CancellationToken ct);

    Task<ApiResult<ProductApiModel>> GetAsync(Guid id, CancellationToken ct);

    Task<ApiResult<ProductApiModel>> CreateAsync(ProductWriteBody body, CancellationToken ct);

    Task<ApiResult<ProductApiModel>> UpdateAsync(Guid id, ProductWriteBody body, CancellationToken ct);

    Task<ApiResult> DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class AdminProductClient(IGatewayClient gateway) : IAdminProductClient
{
    public Task<ApiResult<PagedApiModel<ProductApiModel>>> ListAsync(string? search, string? category, int page, int pageSize, CancellationToken ct)
        => gateway.GetAsync<PagedApiModel<ProductApiModel>>(
            AdminQuery.Build("/gateway/products",
                ("search", search),
                ("category", category),
                ("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("includeInactive", "true")),
            ct: ct);

    public Task<ApiResult<ProductApiModel>> GetAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<ProductApiModel>($"/gateway/products/{id}", ct: ct);

    public Task<ApiResult<ProductApiModel>> CreateAsync(ProductWriteBody body, CancellationToken ct)
        => gateway.PostAsync<ProductApiModel>("/gateway/products", body, ct: ct);

    public Task<ApiResult<ProductApiModel>> UpdateAsync(Guid id, ProductWriteBody body, CancellationToken ct)
        => gateway.PutAsync<ProductApiModel>($"/gateway/products/{id}", body, ct: ct);

    public Task<ApiResult> DeleteAsync(Guid id, CancellationToken ct)
        => gateway.DeleteAsync($"/gateway/products/{id}", ct: ct);
}
