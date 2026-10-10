using System.ComponentModel.DataAnnotations;

namespace Hastane.Identity.Api.Dtos;

public class ForgotPasswordDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    // Identity'nin DataProtector token'ı (base64url, birkaç yüz karakter); üst sınır yalnız kötüye kullanıma karşı.
    [Required, StringLength(2048)]
    public string Token { get; set; } = string.Empty;

    [Required, StringLength(128)]
    public string NewPassword { get; set; } = string.Empty;
}

// Her iki uçta da AYNI sabit gövde (kullanıcı var/yok sızmasın).
public record MessageDto(string Message);
