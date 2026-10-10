namespace Hastane.Market.Api.Entities;

// Sabit kategori listesi; veritabanında ve JSON'da METİN olarak durur ("MedicalDevice"), sayı olarak değil.
// Sıra değişse ya da araya yeni değer girse eski kayıtlar bozulmaz.
public enum ProductCategory
{
    Vitamin = 1,
    Care = 2,
    MedicalDevice = 3,
    MotherBaby = 4,
    FirstAid = 5,
}

public static class ProductCategories
{
    // Enum.TryParse "3" gibi sayıları da kabul eder; yalnız adı kabul ediyoruz.
    public static bool TryParse(string? text, out ProductCategory category)
    {
        category = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var name = Enum.GetNames<ProductCategory>().FirstOrDefault(n => n.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));
        return name is not null && Enum.TryParse(name, out category);
    }
}
