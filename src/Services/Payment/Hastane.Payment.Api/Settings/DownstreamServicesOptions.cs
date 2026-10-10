using System.ComponentModel.DataAnnotations;

namespace Hastane.Payment.Api.Settings;

// Servisten servise çağrı ayarları ("Services" bölümü). Adresler appsettings'te varsayılan geliştirme portlarıdır.
public class DownstreamServicesOptions
{
    public const string SectionName = "Services";

    [Required, Url]
    public string AppointmentBaseUrl { get; set; } = "http://localhost:5104";

    [Required, Url]
    public string DoctorBaseUrl { get; set; } = "http://localhost:5102";

    // Kart çekilmeden ÖNCEKİ doğrulama çağrıları için zaman aşımı: yanıt gelmezse ödeme 503 ile reddedilir.
    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 5;
}
