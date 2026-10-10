namespace Hastane.Identity.Api.Dtos;

// Dışarı yalnız bunlar çıkar. PasswordHash, SecurityStamp, PhoneNumber, kilit bilgisi bilerek yok.
public class UserSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    // Bağlı doktor kaydı (yoksa null).
    public string? DoctorId { get; set; }
    // Göreli avatar adresi (/gateway/users/{id}/photo); fotoğraf yoksa null. Dosya adı dışarı çıkmaz.
    public string? PhotoUrl { get; set; }
}
