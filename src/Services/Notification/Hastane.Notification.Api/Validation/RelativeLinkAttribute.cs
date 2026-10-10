using System.ComponentModel.DataAnnotations;

namespace Hastane.Notification.Api.Validation;

// Bildirim bağlantısı yalnız uygulama içi göreli yol olabilir: "/" ile başlar, "//" ile (protokol-göreli dış adres),
// "/\" ile ya da "javascript:" gibi şemayla başlamaz. Boş/null serbest (bağlantı isteğe bağlı).
[AttributeUsage(AttributeTargets.Property)]
public sealed class RelativeLinkAttribute : ValidationAttribute
{
    public const int MaxLength = 500;

    private const char Backslash = (char)92;

    public static bool IsValidLink(string? link)
    {
        if (string.IsNullOrEmpty(link)) return true;
        if (link.Length > MaxLength) return false;
        if (link[0] != '/') return false;
        if (link.Length > 1 && (link[1] == '/' || link[1] == Backslash)) return false;
        return !link.Any(c => char.IsControl(c) || c == Backslash);
    }

    public override bool IsValid(object? value) => value is null || (value is string s && IsValidLink(s));

    public override string FormatErrorMessage(string name)
        => $"{name} yalnız '/' ile başlayan uygulama içi göreli yol olabilir (en çok {MaxLength} karakter).";
}
