using System.ComponentModel.DataAnnotations;
using Hastane.Notification.Api.Validation;

namespace Hastane.Notification.Api.Dtos;

public class SendEmailDto
{
    [Required, SingleLineEmail]
    public string To { get; set; } = string.Empty;

    // Satır sonu içeremez (başlık enjeksiyonu).
    [Required, StringLength(200, MinimumLength = 1)]
    [RegularExpression(@"^[^\r\n]*\z", ErrorMessage = "Subject tek satır olmalı.")]
    public string Subject { get; set; } = string.Empty;

    [Required, StringLength(20000, MinimumLength = 1)]
    public string TextBody { get; set; } = string.Empty;

    [StringLength(100000)]
    public string? HtmlBody { get; set; }
}
