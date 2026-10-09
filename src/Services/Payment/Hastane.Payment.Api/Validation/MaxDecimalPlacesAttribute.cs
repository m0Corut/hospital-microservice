using System.ComponentModel.DataAnnotations;

namespace Hastane.Payment.Api.Validation;

// Para tutarı en çok N ondalık basamak taşır (12.345 kabul edilmez; sessizce yuvarlamak yanlış tutar tahsil ettirir).
[AttributeUsage(AttributeTargets.Property)]
public sealed class MaxDecimalPlacesAttribute : ValidationAttribute
{
    private readonly int _places;

    public MaxDecimalPlacesAttribute(int places) : base("{0} en çok " + places + " ondalık basamak içerebilir.")
    {
        _places = places;
    }

    public override bool IsValid(object? value) => value is not decimal d || Check(d, _places);

    public static bool Check(decimal value, int places) => decimal.Round(value, places) == value;
}
