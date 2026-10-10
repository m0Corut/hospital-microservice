namespace Hastane.Market.Api.Entities;

public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ProductCategory Category { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = ProductCurrency.Try;
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Eşzamanlılık belirteci: yönetici formu kaydederken araya bir stok düşümü girdiyse güncelleme 409 olur
    // (yönetici eski stok sayısını sessizce geri yazamaz).
    public byte[] RowVersion { get; set; } = [];
}

public static class ProductCurrency
{
    public const string Try = "TRY";
}
