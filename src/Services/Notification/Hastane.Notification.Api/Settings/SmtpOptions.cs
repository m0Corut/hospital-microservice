using System.ComponentModel.DataAnnotations;

namespace Hastane.Notification.Api.Settings;

// SMTP ayarları ("Smtp" bölümü). Geliştirmede MailHog: localhost:1025, TLS ve kimlik doğrulama yok.
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    [Required]
    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 1025;

    [Required]
    public string From { get; set; } = "no-reply@hastane.local";
}
