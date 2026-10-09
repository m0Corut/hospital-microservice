using System.ComponentModel.DataAnnotations;
using Hastane.Appointment.Api.Data;

namespace Hastane.Appointment.Api.Validation;

// Geçmişe randevu açılamaz / taşınamaz. [ApiController] yanıtı otomatik 400 yapar.
[AttributeUsage(AttributeTargets.Property)]
public sealed class FutureDateAttribute : ValidationAttribute
{
    public FutureDateAttribute() : base("Randevu tarihi gelecekte olmalı.")
    {
    }

    public override bool IsValid(object? value)
        => value is DateTime date && UtcDateTimeConverter.Normalize(date) > DateTime.UtcNow;
}
