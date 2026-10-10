using System.ComponentModel.DataAnnotations;

namespace Hastane.Market.Api.Validation;

// Para tutarında en çok N ondalık basamak (1.234 TL gibi kuruş altı fiyat yok).
[AttributeUsage(AttributeTargets.Property)]
public sealed class MaxDecimalPlacesAttribute(int places) : ValidationAttribute($"En çok {places} ondalık basamak olabilir.")
{
    public override bool IsValid(object? value)
        => value is not decimal d || decimal.Round(d, places) == d;
}

// Görsel adresi yalnız yerel yol ("/img/products/x.svg") ya da http/https olabilir: javascript: gibi şemalar girmez.
[AttributeUsage(AttributeTargets.Property)]
public sealed class ProductImageUrlAttribute() : ValidationAttribute("Görsel adresi '/' ile başlayan yerel yol ya da http(s) adresi olmalı.")
{
    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        if (value is not string s) return false;
        if (s.Length == 0) return true;
        if (s.Any(char.IsControl) || s.Contains('\\')) return false;
        if (s.StartsWith('/')) return !s.StartsWith("//", StringComparison.Ordinal);
        return Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    }
}
