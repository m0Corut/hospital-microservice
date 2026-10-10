using Hastane.Market.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Market.Api.Data;

// Geliştirmede mağazanın boş görünmemesi için örnek ürünler. Yalnız tablo BOŞSA ve Market:SeedDemoData=true ise çalışır.
// Görseller WebUI'nin wwwroot/img/products/ klasöründeki kendi çizimimiz SVG'lerdir.
public static class DemoDataSeeder
{
    public static async Task SeedAsync(MarketDbContext db, CancellationToken ct = default)
    {
        if (await db.Products.AnyAsync(ct)) return;

        var now = DateTime.UtcNow;
        Product P(string name, string description, ProductCategory category, decimal price, int stock, string image) => new()
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Description = description,
            Category = category,
            Price = price,
            Currency = ProductCurrency.Try,
            Stock = stock,
            ImageUrl = "/img/products/" + image,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Products.AddRange(
            P("C Vitamini 1000 mg", "Bağışıklık desteği için 60 efervesan tablet.", ProductCategory.Vitamin, 189.90m, 120, "vitamin-c.svg"),
            P("D3 Vitamini Damla", "Günde tek damla; 20 ml damla şişesi.", ProductCategory.Vitamin, 149.50m, 80, "vitamin-d.svg"),
            P("Dijital Ateş Ölçer", "Hızlı ölçüm, hafızalı, esnek uçlu.", ProductCategory.MedicalDevice, 249.00m, 60, "thermometer.svg"),
            P("Koldan Tansiyon Aleti", "Büyük ekranlı, 90 ölçüm hafızalı, otomatik.", ProductCategory.MedicalDevice, 899.00m, 25, "blood-pressure.svg"),
            P("Parmak Tipi Oksimetre", "Nabız ve oksijen satürasyonu ölçümü.", ProductCategory.MedicalDevice, 429.90m, 40, "pulse-oximeter.svg"),
            P("Bebek Bezi 4 Numara (44 adet)", "Cildi kuru tutan ince ve yumuşak bebek bezi.", ProductCategory.MotherBaby, 319.00m, 150, "diapers.svg"),
            P("Anti-Kolik Biberon 240 ml", "Hava emmeyi azaltan özel emzik, BPA içermez.", ProductCategory.MotherBaby, 169.00m, 70, "baby-bottle.svg"),
            P("İlk Yardım Çantası", "Evde ve araçta kullanım için 42 parçalık set.", ProductCategory.FirstAid, 559.00m, 35, "first-aid-kit.svg"),
            P("Yara Bandı Seti (100 adet)", "Su geçirmez, karışık boyutlarda yara bandı.", ProductCategory.FirstAid, 79.90m, 200, "bandages.svg"),
            P("El Antiseptiği 500 ml", "%70 alkollü, pompalı el antiseptiği.", ProductCategory.Care, 99.90m, 180, "hand-sanitizer.svg"));

        await db.SaveChangesAsync(ct);
    }
}
