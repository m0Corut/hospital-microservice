using System.ComponentModel.DataAnnotations;

namespace Hastane.Identity.Api.Dtos;

// Bilerek Role / Id / CreatedAt YOK (mass assignment koruması): istemci bu alanları gönderse bile
// bağlanacak bir özellik olmadığı için yok sayılır. Kayıt olan her zaman Patient olur.
public class RegisterDto
{
    [Required, StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    // Üst sınır: çok uzun şifre hash'lemeyi pahalı yapıp sunucuyu yormasın. Kurallar (8+, rakam, büyük/küçük) Identity'de.
    [Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;
}
