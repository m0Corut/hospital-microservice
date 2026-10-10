using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Hastane.WebUI.Models.Api.Market;
using Hastane.WebUI.ViewModels.Admin;
using Hastane.WebUI.ViewModels.Shared;

namespace Hastane.WebUI.ViewModels.Shop;

// Mağaza metinleri ve biçimleri tek yerde. Sunucu kültüründen bağımsız: para her zaman "1.234,50 ₺".
public static class ShopDisplay
{
    public const int PageSize = 12;
    public const int MaxQuantity = 20;
    public const string PlaceholderImage = "/img/products/placeholder.svg";

    // Sabit kategori listesi (API metin enum'u -> Türkçe ad ve ikon). Sıra vitrindeki sırayla aynı.
    public static readonly IReadOnlyList<(string Key, string Label, string Icon)> Categories =
    [
        ("Vitamin", "Vitamin", "bi-capsule"),
        ("Care", "Bakım", "bi-droplet"),
        ("MedicalDevice", "Medikal Cihaz", "bi-heart-pulse"),
        ("MotherBaby", "Anne-Bebek", "bi-balloon-heart"),
        ("FirstAid", "İlk Yardım", "bi-bandaid"),
    ];

    private static readonly NumberFormatInfo MoneyFormat = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberGroupSizes = [3],
        NumberDecimalDigits = 2,
    };

    public static string CategoryLabel(string? key)
        => Categories.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal)).Label ?? key ?? string.Empty;

    public static string CategoryIcon(string? key)
        => Categories.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal)).Icon ?? "bi-bag";

    // Yalnız bilinen kategori anahtarı geçer (büyük/küçük harf fark etmez); aksi halde null: API'ye 400 gidecek istek kurulmaz.
    public static string? NormalizeCategory(string? key)
        => Categories.Select(c => c.Key).FirstOrDefault(k => string.Equals(k, key?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Money(decimal amount, string currency = "TRY")
        => amount.ToString("N2", MoneyFormat) + " " + (currency == "TRY" ? "₺" : currency);

    public static string Image(string? url) => UiKit.SafeImageUrl(url) ?? PlaceholderImage;

    // "Stokta yok" / "Son 3 adet" / null (rahat stok): kartta rozet olarak gösterilir.
    public static string? StockNote(int stock) => stock switch
    {
        <= 0 => "Stokta yok",
        <= 5 => $"Son {stock} adet",
        _ => null,
    };

    // Parametre olarak gelen ya da kullanıcıdan alınan metni güvenli sınırlara çeker.
    public static string? CleanSearch(string? q)
    {
        var text = q?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length > 100 ? text[..100] : text;
    }
}

// /Shop sorgu dizesi: ?q=&category=&page=
public sealed class ShopFilter
{
    public string? Q { get; set; }
    public string? Category { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class ShopIndexViewModel
{
    public ShopFilter Filter { get; init; } = new();
    public List<ProductApiModel> Items { get; init; } = [];
    public long TotalCount { get; init; }
    public int TotalPages => (int)Math.Max(1, Math.Ceiling(TotalCount / (double)ShopDisplay.PageSize));
    public bool CanBuy { get; init; }
    public bool IsSignedIn { get; init; }

    // Sayfa bağlantıları süzgeci korusun.
    public object PageRoute(int page) => new { q = Filter.Q, category = Filter.Category, page };
}

// Ürün kartı (liste ve ayrıntıda ortak parça): ürün + bu ziyaretçi satın alabilir mi / giriş yapmış mı.
public sealed record ProductCardViewModel(ProductApiModel Product, bool CanBuy, bool IsSignedIn);

public sealed class ProductDetailsViewModel
{
    public ProductApiModel Product { get; init; } = new();
    public bool CanBuy { get; init; }
    public bool IsSignedIn { get; init; }
    public int MaxSelectable => Math.Min(ShopDisplay.MaxQuantity, Math.Max(Product.Stock, 0));
}

public sealed class CartViewModel
{
    public CartApiModel Cart { get; init; } = new();
    public bool HasAvailableItems => Cart.Items.Any(i => i.IsAvailable);
    public bool HasStockIssue => Cart.Items.Any(i => i.IsAvailable && i.Quantity > i.Stock);
}

// Admin ürün formu. Fiyat metin: sunucu kültürü virgül/nokta beklentisini değiştirmesin diye değişmez kültürle okunur.
public sealed class ProductFormViewModel : IValidatableObject
{
    public Guid? Id { get; set; }

    [Display(Name = "Ürün adı")]
    [Required(ErrorMessage = "Ürün adı gerekli.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Ürün adı 2-120 karakter olmalı.")]
    public string? Name { get; set; }

    [Display(Name = "Açıklama")]
    [StringLength(2000, ErrorMessage = "Açıklama en fazla 2000 karakter olabilir.")]
    public string? Description { get; set; }

    [Display(Name = "Kategori")]
    [Required(ErrorMessage = "Kategori seçin.")]
    public string? Category { get; set; }

    [Display(Name = "Fiyat (₺)")]
    [Required(ErrorMessage = "Fiyat gerekli.")]
    public string? Price { get; set; }

    [Display(Name = "Stok")]
    [Required(ErrorMessage = "Stok gerekli.")]
    [Range(0, 100000, ErrorMessage = "Stok 0 ile 100000 arasında olmalı.")]
    public int? Stock { get; set; }

    [Display(Name = "Görsel adresi")]
    [StringLength(300, ErrorMessage = "Görsel adresi en fazla 300 karakter olabilir.")]
    public string? ImageUrl { get; set; }

    [Display(Name = "Satışta")]
    public bool IsActive { get; set; } = true;

    public bool IsEdit => Id is not null;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ShopDisplay.NormalizeCategory(Category) is null)
            yield return new ValidationResult("Geçerli bir kategori seçin.", [nameof(Category)]);

        if (ParsePrice(Price) is not { } price || price <= 0 || price > 1_000_000)
            yield return new ValidationResult("Fiyat 0,01 ile 1.000.000 arasında, en çok 2 ondalıklı olmalı.", [nameof(Price)]);

        if (!string.IsNullOrWhiteSpace(ImageUrl) && UiKit.SafeImageUrl(ImageUrl) is null)
            yield return new ValidationResult("Görsel adresi '/' ile başlayan yerel yol ya da http(s) adresi olmalı.", [nameof(ImageUrl)]);
    }

    // "149,90" ve "149.90" kabul; en çok 2 ondalık. Binlik ayracı yok.
    public static decimal? ParsePrice(string? text)
    {
        var normalized = text?.Trim().Replace(',', '.');
        if (string.IsNullOrEmpty(normalized)) return null;
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return null;
        return decimal.Round(value, 2) == value ? value : null;
    }

    public static ProductFormViewModel From(ProductApiModel p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        Category = p.Category,
        Price = p.Price.ToString("0.00", CultureInfo.InvariantCulture),
        Stock = p.Stock,
        ImageUrl = p.ImageUrl,
        IsActive = p.IsActive,
    };

    public ProductWriteBody ToBody() => new()
    {
        Name = Name!.Trim(),
        Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        Category = ShopDisplay.NormalizeCategory(Category)!,
        Price = ParsePrice(Price)!.Value,
        Stock = Stock!.Value,
        ImageUrl = string.IsNullOrWhiteSpace(ImageUrl) ? null : ImageUrl.Trim(),
        IsActive = IsActive,
    };
}

public sealed class AdminProductListViewModel
{
    public List<ProductApiModel> Items { get; init; } = [];
    public PagerViewModel Pager { get; init; } = new();
    public string? Search { get; init; }
    public string? Category { get; init; }
}
