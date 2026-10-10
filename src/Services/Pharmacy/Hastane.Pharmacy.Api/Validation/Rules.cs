using System.Text.RegularExpressions;
using Hastane.Pharmacy.Api.Entities;

namespace Hastane.Pharmacy.Api.Validation;

// Küçük kural kütüphanesi: validator sınıfları bunları çağırır. Mesajlar Türkçe (arayüz form hatası olarak gösterir).
// Property adı, istek özelliğinin adıyla aynı (PascalCase): WebUI hatayı aynı adlı form alanına bağlar.
public static partial class Rules
{
    public static void Required(List<ValidationFailure> failures, string property, string label, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            failures.Add(new(property, $"{label} gerekli."));
        else if (value.Trim().Length > maxLength)
            failures.Add(new(property, $"{label} en çok {maxLength} karakter olabilir."));
    }

    public static void Barcode(List<ValidationFailure> failures, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(new("Barcode", "Barkod gerekli."));
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 20) failures.Add(new("Barcode", "Barkod en çok 20 karakter olabilir."));
        else if (!BarcodePattern().IsMatch(trimmed)) failures.Add(new("Barcode", "Barkod yalnız harf, rakam ve '-' içerebilir."));
    }

    public static void Price(List<ValidationFailure> failures, decimal value)
    {
        if (value < 0) failures.Add(new("Price", "Fiyat eksi olamaz."));
        else if (value > 9_999_999.99m) failures.Add(new("Price", "Fiyat çok büyük."));
        else if (decimal.Round(value, 2) != value) failures.Add(new("Price", "Fiyat en çok 2 ondalık basamak içerebilir."));
    }

    public static void NonNegative(List<ValidationFailure> failures, string property, string label, int value)
    {
        if (value < 0) failures.Add(new(property, $"{label} eksi olamaz."));
    }

    public static void Form(List<ValidationFailure> failures, MedicineForm value)
    {
        if (!Enum.IsDefined(value)) failures.Add(new("Form", "Geçersiz ilaç formu."));
    }

    public static void Paging(List<ValidationFailure> failures, int page, int pageSize)
    {
        if (page < 1) failures.Add(new("Page", "Sayfa 1 ya da daha büyük olmalı."));
        if (pageSize is < 1 or > 100) failures.Add(new("PageSize", "Sayfa boyutu 1 ile 100 arasında olmalı."));
    }

    [GeneratedRegex("^[A-Za-z0-9-]+$")]
    private static partial Regex BarcodePattern();
}
