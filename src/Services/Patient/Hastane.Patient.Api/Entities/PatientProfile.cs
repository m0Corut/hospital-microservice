namespace Hastane.Patient.Api.Entities;

// Anahtar UserId: Identity'deki kullanıcı kimliği (token'ın sub'ı). Hasta başına tek profil; ayrı Id üretmeye gerek yok.
public class PatientProfile
{
    public string UserId { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public BloodType? BloodType { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public DateTime UpdatedAt { get; set; }
}
