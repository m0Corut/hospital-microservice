using System.ComponentModel.DataAnnotations;

namespace Hastane.Market.Api.Dtos;

// Sepet satırı sayısı 1-20 arası; 0 = ürünü sepetten çıkar. (Aralık serviste de denetlenir: tek doğru kaynak CartRules.)
public class SetCartItemDto
{
    [Range(0, 20)]
    public int Quantity { get; set; }
}

// Sepette yalnız productId + quantity saklanır. Ad, fiyat ve satışta olup olmadığı HER okumada ürün kaydından gelir:
// fiyat değişirse sepet güncel fiyatı gösterir.
public class CartItemDto
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }

    // false: ürün pasife alındı ya da silindi ("artık satışta değil"). Toplama katılmaz, UnitPrice/LineTotal 0 döner.
    public bool IsAvailable { get; set; } = true;
    public string? Note { get; set; }

    // Ek bilgi: arayüzün "stok yetersiz" uyarısı ve görseli için.
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
}

public class CartResultDto
{
    public List<CartItemDto> Items { get; set; } = [];
    public decimal Total { get; set; }
    public string Currency { get; set; } = "TRY";
}
