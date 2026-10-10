using Hastane.Market.Api.Caching;
using Hastane.Market.Api.Data;
using Hastane.Market.Api.Dtos;
using Hastane.Market.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Market.Api.Services;

public interface ICartService
{
    Task<OperationResult<CartResultDto>> GetAsync(string userId, CancellationToken ct);

    // quantity 0 = sil. Dönen değer güncel sepettir.
    Task<OperationResult<CartResultDto>> SetItemAsync(string userId, Guid productId, int quantity, CancellationToken ct);

    Task<OperationResult<bool>> ClearAsync(string userId, CancellationToken ct);
}

public static class CartRules
{
    public const int MaxQuantity = 20;
}

public class CartService(ICartStore store, MarketDbContext db) : ICartService
{
    public async Task<OperationResult<CartResultDto>> GetAsync(string userId, CancellationToken ct)
    {
        try
        {
            return OperationResult<CartResultDto>.Ok(await BuildAsync(userId, ct));
        }
        catch (RedisUnavailableException)
        {
            return Unavailable();
        }
    }

    public async Task<OperationResult<CartResultDto>> SetItemAsync(string userId, Guid productId, int quantity, CancellationToken ct)
    {
        if (quantity < 0 || quantity > CartRules.MaxQuantity)
            return OperationResult<CartResultDto>.Validation($"Miktar 0 ile {CartRules.MaxQuantity} arasında olmalı (0 = sepetten çıkar).");

        try
        {
            if (quantity > 0)
            {
                // Ekleme/artırma yolunda ürün ŞİMDİ satışta ve stokta olmalı. (Silme yolu ürüne bakmaz: silinmiş ürün de temizlenebilsin.)
                var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct);
                if (product is null || !product.IsActive)
                    return OperationResult<CartResultDto>.NotFound("Ürün bulunamadı ya da artık satışta değil.");
                if (quantity > product.Stock)
                    return OperationResult<CartResultDto>.Conflict($"Yetersiz stok: en fazla {product.Stock} adet eklenebilir.");
            }

            await store.SetAsync(userId, productId, quantity, ct);
            return OperationResult<CartResultDto>.Ok(await BuildAsync(userId, ct));
        }
        catch (RedisUnavailableException)
        {
            return Unavailable();
        }
    }

    public async Task<OperationResult<bool>> ClearAsync(string userId, CancellationToken ct)
    {
        try
        {
            await store.ClearAsync(userId, ct);
            return OperationResult<bool>.Ok(true);
        }
        catch (RedisUnavailableException)
        {
            return OperationResult<bool>.ServiceUnavailable("Sepet servisi şu an kullanılamıyor.");
        }
    }

    // Sepet = Redis'teki (productId, adet) çiftleri + her okumada SQL'deki güncel ürün kaydı.
    private async Task<CartResultDto> BuildAsync(string userId, CancellationToken ct)
    {
        var entries = await store.GetAsync(userId, ct);
        var ids = entries.Keys.ToList();
        var products = ids.Count == 0
            ? new Dictionary<Guid, Product>()
            : await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var items = new List<CartItemDto>();
        foreach (var (productId, quantity) in entries)
        {
            if (products.TryGetValue(productId, out var p) && p.IsActive)
            {
                items.Add(new CartItemDto
                {
                    ProductId = productId,
                    Name = p.Name,
                    UnitPrice = p.Price,
                    Quantity = quantity,
                    LineTotal = p.Price * quantity,
                    IsAvailable = true,
                    Stock = p.Stock,
                    ImageUrl = p.ImageUrl,
                });
            }
            else
            {
                // Pasif ya da silinmiş: satırı gösteriyoruz (kullanıcı neden kaybolduğunu görsün) ama toplama katmıyoruz.
                items.Add(new CartItemDto
                {
                    ProductId = productId,
                    Name = p?.Name ?? "Silinmiş ürün",
                    UnitPrice = 0,
                    Quantity = quantity,
                    LineTotal = 0,
                    IsAvailable = false,
                    Note = "Artık satışta değil",
                    Stock = 0,
                    ImageUrl = p?.ImageUrl,
                });
            }
        }

        items = items.OrderByDescending(i => i.IsAvailable).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.ProductId).ToList();
        return new CartResultDto
        {
            Items = items,
            Total = items.Where(i => i.IsAvailable).Sum(i => i.LineTotal),
            Currency = ProductCurrency.Try,
        };
    }

    private static OperationResult<CartResultDto> Unavailable()
        => OperationResult<CartResultDto>.ServiceUnavailable("Sepet servisi şu an kullanılamıyor. Biraz sonra tekrar deneyin.");
}
