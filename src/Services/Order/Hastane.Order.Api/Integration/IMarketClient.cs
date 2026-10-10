namespace Hastane.Order.Api.Integration;

public enum MarketOutcome
{
    Ok,
    NotFound,    // 404: ürün yok (ya da sepet yok)
    Unavailable, // erişilemedi, zaman aşımı, 5xx, beklenmeyen yanıt
}

public record MarketResult<T>(MarketOutcome Outcome, T? Value = default) where T : class
{
    public static MarketResult<T> Ok(T value) => new(MarketOutcome.Ok, value);
    public static MarketResult<T> NotFound() => new(MarketOutcome.NotFound);
    public static MarketResult<T> Unavailable() => new(MarketOutcome.Unavailable);
}

public enum ReserveOutcome
{
    Reserved,
    InsufficientStock, // Market 409 verdi: hiçbir ürün düşülmedi
    Unavailable,       // sonuç BELİRSİZ: istek Market'e ulaşmış ama yanıt gelmemiş olabilir
}

// Market'in yanıtından yalnız Order'ın ihtiyacı olan alanlar (ProjectReference yok, kendi küçük modelimiz).
public record CartItemInfo(Guid ProductId, string Name, decimal UnitPrice, int Quantity, decimal LineTotal);

public record CartInfo(List<CartItemInfo> Items, decimal Total, string Currency);

public record ProductInfo(Guid Id, string Name, decimal Price, string Currency, int Stock, bool IsActive);

public record ReserveItem(Guid ProductId, int Quantity);

public interface IMarketClient
{
    // Kullanıcının sepeti: gelen isteğin Authorization başlığı aynen iletilir (sepet kullanıcıya özeldir).
    Task<MarketResult<CartInfo>> GetCartAsync(CancellationToken ct);

    // Anonim uç: fiyat/aktiflik doğrusu burada, sepetteki fiyata güvenilmez.
    Task<MarketResult<ProductInfo>> GetProductAsync(Guid productId, CancellationToken ct);

    // İç uç (X-Internal-Key). Aynı orderId ikinci kez gelirse Market 200 verir (idempotent).
    Task<ReserveOutcome> ReserveAsync(Guid orderId, IReadOnlyList<ReserveItem> items, CancellationToken ct);

    // Başarılı siparişten sonra sepeti boşaltır. false = boşaltılamadı (sipariş yine başarılı sayılır).
    Task<bool> ClearCartAsync(CancellationToken ct);
}
