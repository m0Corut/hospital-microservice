using System.ComponentModel.DataAnnotations;

namespace Hastane.Payment.Api.Validation;

// JSON'da alan hiç gönderilmezse Guid varsayılanı 00000000-... olur; [Required] bunu yakalamaz.
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotEmptyGuidAttribute : ValidationAttribute
{
    public NotEmptyGuidAttribute() : base("{0} boş olamaz.")
    {
    }

    public override bool IsValid(object? value) => value is Guid g && g != Guid.Empty;
}
