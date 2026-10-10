using System.ComponentModel.DataAnnotations;
using Hastane.Notification.Api.Validation;

namespace Hastane.Notification.Api.Dtos;

// İç uç gövdesi. UserId ile DoctorId'den TAM BİRİ dolu olmalı (controller denetler).
public class CreateNotificationDto
{
    [MaxLength(100)]
    public string? UserId { get; set; }

    [MaxLength(100)]
    public string? DoctorId { get; set; }

    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(1000, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;

    [RelativeLink]
    public string? Link { get; set; }

    // Küçük harf/rakam/-/_ ; boşsa "info".
    [StringLength(50)]
    [RegularExpression(@"^[a-z0-9_-]*\z", ErrorMessage = "Kind yalnız küçük harf, rakam, '-' ve '_' içerebilir.")]
    public string? Kind { get; set; }
}
