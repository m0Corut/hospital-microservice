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
}
