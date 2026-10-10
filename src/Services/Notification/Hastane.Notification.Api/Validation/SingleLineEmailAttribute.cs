using System.ComponentModel.DataAnnotations;
using MimeKit;

namespace Hastane.Notification.Api.Validation;

// Tek, düz e-posta adresi ("ad@alan"): görünen ad, birden çok alıcı ve satır sonu (başlık enjeksiyonu) kabul edilmez.
[AttributeUsage(AttributeTargets.Property)]
public sealed class SingleLineEmailAttribute : ValidationAttribute
{
    public static bool IsValidAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > 254) return false;
        if (address.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;
        return MailboxAddress.TryParse(address, out var parsed)
               && parsed.Address == address
               && address.Contains('@');
    }

    public override bool IsValid(object? value) => value is string s && IsValidAddress(s);

    public override string FormatErrorMessage(string name) => $"{name} geçerli bir e-posta adresi olmalı.";
}
