using System.ComponentModel.DataAnnotations;

namespace Hastane.Identity.Api.Dtos;

// Bilerek yalnız ad ve soyad: Id token'dan gelir; e-posta, rol, doktor bağı bu uçtan DEĞİŞMEZ (mass assignment koruması).
public class UpdateProfileDto
{
    [Required, StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string LastName { get; set; } = string.Empty;
}
