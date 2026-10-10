using Hastane.WebUI.Infrastructure.Gateway;

namespace Hastane.WebUI.Clients;

// Üst menüdeki sepet rozeti için toplam adet. Menü bir istekte birden çok yerde çizilebilir; sayı istek başına BİR kez
// sorulur (scoped önbellek). Servis kapalı (Redis yok) ya da yetkisizse null: rozet gizlenir, sayfa bozulmaz.
public interface IShopCartCountClient
{
    Task<int?> CountAsync(CancellationToken ct);
}

public sealed class ShopCartCountClient(IShopCartClient cart) : IShopCartCountClient
{
    private Task<int?>? _count;

    public Task<int?> CountAsync(CancellationToken ct) => _count ??= LoadAsync(ct);

    private async Task<int?> LoadAsync(CancellationToken ct)
    {
        var result = await cart.GetAsync(ApiFailures.All, ct);
        // Satışta olmayan ürünler sayıya girmez (toplama da girmiyor).
        return result.TryGetValue(out var model) ? model.Items.Where(i => i.IsAvailable).Sum(i => i.Quantity) : null;
    }
}
