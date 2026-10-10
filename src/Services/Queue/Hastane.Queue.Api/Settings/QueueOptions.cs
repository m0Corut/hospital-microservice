using System.ComponentModel.DataAnnotations;

namespace Hastane.Queue.Api.Settings;

// Servisten servise çağrı ayarları ("Services" bölümü).
public class DownstreamServicesOptions
{
    public const string SectionName = "Services";

    [Required, Url]
    public string AppointmentBaseUrl { get; set; } = "http://localhost:5104";

    // Randevu doğrulanamazsa check-in 503 ile reddedilir; sonsuza kadar beklemeyiz.
    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 5;
}

// "Bugün" kliniğin yerel gününe göre hesaplanır (UTC günü değil).
public class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    [Required]
    public string TimeZone { get; set; } = "Europe/Istanbul";
}
