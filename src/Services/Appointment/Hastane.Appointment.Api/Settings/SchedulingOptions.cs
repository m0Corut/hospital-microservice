using System.ComponentModel.DataAnnotations;

namespace Hastane.Appointment.Api.Settings;

// Doktorun haftalık programı "yerel klinik saati" ile yazılır (Doctor: "09:00"). Randevular ise UTC saklanır.
// İkisini birleştirmek için tek bir saat dilimi gerekir; proje varsayımı Türkiye. Ayara taşındı ki ileride
// çok ülkeli olursa kod değil ayar değişsin (gerçek çözüm: her ClinicLocation'ın kendi saat dilimi).
public class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    [Required]
    public string TimeZone { get; set; } = "Europe/Istanbul";
}
