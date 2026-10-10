using System.ComponentModel.DataAnnotations;

namespace Hastane.Patient.Api.Settings;

// Servisten servise çağrı ayarları ("Services" bölümü).
public class DownstreamServicesOptions
{
    public const string SectionName = "Services";

    [Required, Url]
    public string AppointmentBaseUrl { get; set; } = "http://localhost:5104";

    [Required, Url]
    public string DoctorBaseUrl { get; set; } = "http://localhost:5102";

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 5;
}
