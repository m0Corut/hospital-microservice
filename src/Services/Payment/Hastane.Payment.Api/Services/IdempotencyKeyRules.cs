using System.Text.RegularExpressions;

namespace Hastane.Payment.Api.Services;

// Anahtar tahmin edilemez olmalı (UUID gibi): "1" gibi kısa anahtarlar farklı kullanıcıların ödemelerini çakıştırır.
public static partial class IdempotencyKeyRules
{
    public const string Message = "Idempotency-Key 16-100 karakter olmalı ve yalnız harf, rakam, '-' ve '_' içermeli (ör. UUID).";

    [GeneratedRegex("^[A-Za-z0-9_-]{16,100}$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? key) => key is not null && Pattern().IsMatch(key);
}
