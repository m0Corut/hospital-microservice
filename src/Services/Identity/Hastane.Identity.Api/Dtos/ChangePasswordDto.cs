using System.ComponentModel.DataAnnotations;

namespace Hastane.Identity.Api.Dtos;

public class ChangePasswordDto
{
    [Required, StringLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    // Üst sınır: çok uzun şifre hash'lemeyi pahalı yapmasın. Kurallar (8+, rakam, büyük/küçük) Identity'de.
    [Required, StringLength(128)]
    public string NewPassword { get; set; } = string.Empty;
}
