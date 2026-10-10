using Hastane.Patient.Api.Entities;

namespace Hastane.Patient.Api.Dtos;

public class PatientProfileDto
{
    public string UserId { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    // Doğum tarihinden sunucuda hesaplanır, saklanmaz (her yıl kendiliğinden doğru kalır).
    public int? Age { get; set; }
    public Gender? Gender { get; set; }
    public BloodType? BloodType { get; set; }
    // "A Rh+" gibi ekran metni.
    public string? BloodTypeDisplay { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    // Kayıt hiç yoksa null.
    public DateTime? UpdatedAt { get; set; }
}
