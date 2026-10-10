using System.ComponentModel.DataAnnotations;

namespace Hastane.Patient.Api.Validation;

// Doğum tarihi 1900-01-01 ile bugün (UTC) arasında olmalı. Boş (null) geçerlidir: alan isteğe bağlı.
[AttributeUsage(AttributeTargets.Property)]
public sealed class BirthDateAttribute : ValidationAttribute
{
    public static readonly DateOnly Earliest = new(1900, 1, 1);

    public BirthDateAttribute() : base("Doğum tarihi 1900'den sonra ve gelecekte olmayan bir tarih olmalı.")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        if (value is not DateOnly date) return false;
        return date >= Earliest && date <= DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
