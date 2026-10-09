using System.ComponentModel.DataAnnotations;

namespace Hastane.Doctor.Api.Validation;

// Liste ÖĞELERİNİ doğrulayan öznitelikler ([MaxLength] yalnız öğe SAYISINI sınar).

[AttributeUsage(AttributeTargets.Property)]
public sealed class EachMaxLengthAttribute(int max) : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is not IEnumerable<string?> items) return ValidationResult.Success;
        foreach (var item in items)
            if (string.IsNullOrWhiteSpace(item) || item.Length > max)
                return new ValidationResult($"{context.MemberName}: her öğe boş olmamalı ve en çok {max} karakter olmalı.");
        return ValidationResult.Success;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class EachHttpUrlAttribute(int maxLength) : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is not IEnumerable<string?> items) return ValidationResult.Success;
        foreach (var item in items)
            if (!DoctorRules.IsHttpUrl(item, maxLength))
                return new ValidationResult($"{context.MemberName}: her öğe en çok {maxLength} karakterlik geçerli bir http/https adresi olmalı.");
        return ValidationResult.Success;
    }
}

// Boşsa geçer; doluysa http/https URL ve uzunluk sınırı.
[AttributeUsage(AttributeTargets.Property)]
public sealed class OptionalHttpUrlAttribute(int maxLength) : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        var s = value as string;
        if (string.IsNullOrEmpty(s)) return ValidationResult.Success;
        return DoctorRules.IsHttpUrl(s, maxLength)
            ? ValidationResult.Success
            : new ValidationResult($"{context.MemberName}: en çok {maxLength} karakterlik geçerli bir http/https adresi olmalı.");
    }
}
