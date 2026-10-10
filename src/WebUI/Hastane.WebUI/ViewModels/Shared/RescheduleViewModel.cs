using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Hastane.WebUI.ViewModels.Shared;

// Yeniden planlama formu (hasta ve doktor paneli ortak). Şube, doktor ve durum formdan ALINMAZ: her istekte API'den okunur.
public sealed class RescheduleViewModel
{
    public Guid Id { get; set; }

    // Yerel tarih-saat (datetime-local). Sunucu UTC'ye çevirip API'ye yollar.
    [Required(ErrorMessage = "Tarih ve saat seçin.")]
    [Display(Name = "Yeni tarih ve saat")]
    public DateTime? AppointmentLocal { get; set; }

    // Yalnız gösterim (POST'ta yeniden doldurulur).
    public string PartyLabel { get; set; } = string.Empty;
    public DateTime CurrentDate { get; set; }
}

public static class AppointmentTime
{
    // Yerel saati UTC'ye çevirir; geçmiş tarih formda hata olur (API'ye hiç gitmez).
    public static DateTime? LocalToFutureUtc(DateTime? local, ModelStateDictionary state, string key)
    {
        if (local is not { } value) return null;
        var utc = DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime();
        if (utc > DateTime.UtcNow) return utc;
        state.AddModelError(key, "Randevu tarihi gelecekte olmalı.");
        return null;
    }
}
