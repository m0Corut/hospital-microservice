using System.ComponentModel.DataAnnotations;

namespace Hastane.Notification.Api.Settings;

// Klinik saat dilimi ("Scheduling" bölümü): olay UTC tarih taşır, bildirim metni klinik saatiyle yazılır
// (Appointment ve Queue ile aynı anahtar ve varsayılan).
public class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    [Required]
    public string TimeZone { get; set; } = "Europe/Istanbul";
}
