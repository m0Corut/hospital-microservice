using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Hastane.WebUI.Models.Api.Admin;
using Hastane.WebUI.Models.Api.Pharmacy;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Hastane.WebUI.ViewModels.Pharmacy;

public static class PharmacyDisplay
{
    public static readonly string[] Forms = ["Tablet", "Syrup", "Cream", "Ampoule", "Capsule", "Other"];

    public static string Form(string form) => form switch
    {
        "Tablet" => "Tablet",
        "Syrup" => "Şurup",
        "Cream" => "Krem",
        "Ampoule" => "Ampul",
        "Capsule" => "Kapsül",
        "Other" => "Diğer",
        _ => form,
    };

    public static List<SelectListItem> FormChoices(string? selected)
        => Forms.Select(f => new SelectListItem(Form(f), f, f == selected)).ToList();

    public static string MovementType(string type) => type switch
    {
        "Initial" => "İlk stok",
        "Adjustment" => "Elle düzeltme",
        "Dispensation" => "Reçete karşılama",
        _ => type,
    };

    public static string LineStatus(string status) => status switch
    {
        "Matched" => "Karşılanabilir",
        "NotFound" => "Katalogda yok",
        "Ambiguous" => "Birden çok eşleşme",
        "InsufficientStock" => "Stok yetersiz",
        _ => status,
    };

    public static string LineBadge(string status) => status == "Matched" ? "text-bg-success" : "text-bg-danger";

    public static string Money(decimal amount, string currency = "TRY")
        => $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {currency}";

    public static string Signed(int delta) => delta > 0 ? "+" + delta.ToString(CultureInfo.InvariantCulture) : delta.ToString(CultureInfo.InvariantCulture);

    // Fiyat metni: "25,50" ve "25.50" kabul edilir (form tr-TR kullanıcısı virgül yazabilir); sunucu kültürüne bağlı değil.
    public static decimal? ParsePrice(string? text)
        => decimal.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;

    public static string FormatPrice(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}

public sealed class PharmacyMedicineFilterViewModel
{
    public string? Search { get; set; }
    public string? Form { get; set; }
}

public sealed class PharmacyMedicineIndexViewModel
{
    public required PharmacyMedicineFilterViewModel Filter { get; init; }
    public required AdminPagedApiModel<PharmacyMedicineApiModel> Page { get; init; }
}

public sealed class PharmacyMedicineFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "İlaç adı gerekli."), StringLength(150, ErrorMessage = "İlaç adı en çok 150 karakter olabilir.")]
    [Display(Name = "İlaç adı")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Etken madde gerekli."), StringLength(150, ErrorMessage = "Etken madde en çok 150 karakter olabilir.")]
    [Display(Name = "Etken madde")]
    public string? ActiveIngredient { get; set; }

    [Required(ErrorMessage = "Form gerekli.")]
    [Display(Name = "Form")]
    public string? Form { get; set; } = "Tablet";

    [Required(ErrorMessage = "Doz gerekli."), StringLength(50, ErrorMessage = "Doz en çok 50 karakter olabilir.")]
    [Display(Name = "Doz (ör. 500 mg)")]
    public string? Strength { get; set; }

    [Required(ErrorMessage = "Barkod gerekli."), StringLength(20, ErrorMessage = "Barkod en çok 20 karakter olabilir.")]
    [Display(Name = "Barkod")]
    public string? Barcode { get; set; }

    // Metin: "25,50" da "25.50" de girilebilir (bkz. PharmacyDisplay.ParsePrice).
    [Required(ErrorMessage = "Fiyat gerekli.")]
    [Display(Name = "Fiyat (TRY)")]
    public string? Price { get; set; }

    // Yalnız oluştururken; düzenlemede stok ayrı ekrandan (hareket kaydıyla) değişir.
    [Range(0, int.MaxValue, ErrorMessage = "Stok eksi olamaz.")]
    [Display(Name = "Başlangıç stoğu")]
    public int Stock { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Asgari stok eksi olamaz.")]
    [Display(Name = "Asgari stok")]
    public int MinStock { get; set; }

    [Display(Name = "Reçeteli satılır")]
    public bool RequiresPrescription { get; set; }

    // Düzenleme açıldığındaki sürüm: PUT'ta geri gönderilir, başkası arada değiştirdiyse API 409 verir.
    public string? RowVersion { get; set; }

    public string? CurrentStock { get; set; }

    public static PharmacyMedicineFormViewModel From(PharmacyMedicineApiModel m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        ActiveIngredient = m.ActiveIngredient,
        Form = m.Form,
        Strength = m.Strength,
        Barcode = m.Barcode,
        Price = PharmacyDisplay.FormatPrice(m.Price),
        MinStock = m.MinStock,
        RequiresPrescription = m.RequiresPrescription,
        RowVersion = m.RowVersion,
        CurrentStock = m.Stock.ToString(CultureInfo.InvariantCulture),
    };
}

public sealed class PharmacyStockViewModel
{
    public Guid Id { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public int CurrentStock { get; set; }

    [Required(ErrorMessage = "Değişim miktarı gerekli.")]
    [Display(Name = "Değişim (+giriş / -çıkış)")]
    public int? Delta { get; set; }

    [Required(ErrorMessage = "Neden gerekli."), StringLength(200, ErrorMessage = "Neden en çok 200 karakter olabilir.")]
    [Display(Name = "Neden")]
    public string? Reason { get; set; }
}

public sealed class PharmacyMovementsViewModel
{
    public required PharmacyMedicineApiModel Medicine { get; init; }
    public required AdminPagedApiModel<PharmacyMovementApiModel> Page { get; init; }
}

public sealed class PharmacyDispenseViewModel
{
    [Display(Name = "Reçete Id")]
    public string? PrescriptionId { get; set; }

    public PharmacyDispensePreviewApiModel? Preview { get; set; }
}

public sealed class PharmacyDispensationsViewModel
{
    public required AdminPagedApiModel<PharmacyDispensationApiModel> Page { get; init; }
}
