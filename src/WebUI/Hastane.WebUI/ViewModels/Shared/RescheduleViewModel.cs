using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Hastane.WebUI.ViewModels.Shared;

// Yeniden planlama formu (hasta ve doktor paneli ortak). Şube, doktor ve durum formdan ALINMAZ: her istekte API'den okunur.
public sealed class RescheduleViewModel
{
    public Guid Id { get; set; }

    // Seçilen slotun UTC başlangıcı ("yyyy-MM-ddTHH:mm:ssZ"): saat seçici radio'sundan gelir, serbest tarih girişi yok.
    [Required(ErrorMessage = "Bir saat seçin.")]
    [Display(Name = "Yeni saat")]
    public string? SlotStart { get; set; }

    // Gösterilen gün (yyyy-MM-dd); hatayla dönüşte aynı gün yeniden çizilir.
    public string? Date { get; set; }

    // Yalnız gösterim (POST'ta yeniden doldurulur).
    public string PartyLabel { get; set; } = string.Empty;
    public DateTime CurrentDate { get; set; }

    [BindNever]
    public SlotPickerViewModel Picker { get; set; } = new();
}
