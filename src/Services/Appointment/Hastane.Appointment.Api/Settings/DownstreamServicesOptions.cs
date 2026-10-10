using System.ComponentModel.DataAnnotations;

namespace Hastane.Appointment.Api.Settings;

// Servisten servise çağrı ayarları ("Services" bölümü). Adres, appsettings'te varsayılan geliştirme portudur.
public class DownstreamServicesOptions
{
    public const string SectionName = "Services";

    [Required, Url]
    public string DoctorBaseUrl { get; set; } = "http://localhost:5102";

    // Doktor programı gelmezse randevu açılmaz / boş saat hesaplanmaz (503); sonsuza kadar beklemeyiz.
    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 5;

    // Bildirim servisi (iç uç). Kapalı/yavaşsa randevu işlemi etkilenmez; yalnız bu süre kadar beklenir.
    [Required, Url]
    public string NotificationBaseUrl { get; set; } = "http://localhost:5112";

    [Range(1, 30)]
    public int NotificationTimeoutSeconds { get; set; } = 3;
}
