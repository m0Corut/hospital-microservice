namespace Hastane.WebUI.Models.Api.Market;

// Market servisinin JSON'unun WebUI karşılığı (servis DTO'su kopyalanmadı, elle yazıldı).
public sealed class ProductApiModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Vitamin | Care | MedicalDevice | MotherBaby | FirstAid
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "TRY";
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CartItemApiModel
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }

    // false: ürün pasife alındı ya da silindi; toplama katılmaz.
    public bool IsAvailable { get; set; } = true;
    public string? Note { get; set; }
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
}

public sealed class CartApiModel
{
    public List<CartItemApiModel> Items { get; set; } = [];
    public decimal Total { get; set; }
    public string Currency { get; set; } = "TRY";
}

// PUT /cart/items/{productId} gövdesi: adet 0 = sepetten çıkar.
public sealed class SetCartItemBody
{
    public int Quantity { get; set; }
}

// POST / PUT /products gövdesi. Para birimi gönderilmez (servis TRY'dir).
public sealed class ProductWriteBody
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
}
