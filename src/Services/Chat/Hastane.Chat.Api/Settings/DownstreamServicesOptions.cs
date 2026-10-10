using System.ComponentModel.DataAnnotations;

namespace Hastane.Chat.Api.Settings;

// Servisten servise çağrı ayarları ("Services" bölümü). Payment'taki desenin aynısı.
public class DownstreamServicesOptions
{
    public const string SectionName = "Services";

    [Required, Url]
    public string AppointmentBaseUrl { get; set; } = "http://localhost:5104";

    [Required, Url]
    public string DoctorBaseUrl { get; set; } = "http://localhost:5102";

    [Required, Url]
    public string IdentityBaseUrl { get; set; } = "http://localhost:5001";

    // Yanıt gelmezse çağrı "ulaşılamadı" sayılır (konuşma açma 503'e düşer; hasta adı için "Hasta" kullanılır).
    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 5;
}
