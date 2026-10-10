using Microsoft.AspNetCore.Identity;

namespace Hastane.Identity.Api.Entities;

// IdentityUser hazır alanları getirir (Id, Email, PasswordHash, SecurityStamp, kilit bilgisi...).
// Biz yalnız hastaneye özgü alanları ekleriz. Id, Identity'nin string Guid'idir.
public class AppUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    // Sunucu atar (UTC); istemci gönderemez.
    public DateTime CreatedAt { get; set; }

    // Doctor servisindeki (MongoDB) doktor kaydının kimliği. Yalnız Admin bağlar (PUT users/{id}/doctor-link).
    // Token'a doctor_id claim'i olarak buradan girer. Doctor servisine sorulmaz: kimlik metin olarak saklanır.
    public string? DoctorId { get; set; }

    // Profil fotoğrafının SUNUCUNUN ürettiği dosya adı ({userId}-{guid}.{ext}); yoksa null. Yol Storage:PhotoPath altındadır.
    public string? PhotoFileName { get; set; }
}
