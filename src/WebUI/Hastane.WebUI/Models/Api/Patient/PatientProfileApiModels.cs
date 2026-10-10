namespace Hastane.WebUI.Models.Api.Patient;

// Patient servisinin profil DTO'sunun WebUI kopyası. Cinsiyet ve kan grubu metin gelir/gider ("Male", "APos");
// "A Rh+" gibi ekran metni BloodTypeDisplay alanında sunucudan gelir.
public sealed class PatientProfileApiModel
{
    public string UserId { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    // Doğum tarihinden sunucuda hesaplanır, WebUI hesaplamaz.
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? BloodType { get; set; }
    public string? BloodTypeDisplay { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

// PUT /patients/me = profilin TAM değiştirilmesi: gönderilmeyen alan boşaltılır. UserId gövdede yok (token'dan).
public sealed record UpdatePatientProfileRequest(
    DateOnly? DateOfBirth, string? Gender, string? BloodType, string? Phone, string? Address, string? City,
    string? EmergencyContactName, string? EmergencyContactPhone);

public sealed class FavoriteDoctorApiModel
{
    public string DoctorId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
