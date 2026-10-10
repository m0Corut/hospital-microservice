using System.ComponentModel.DataAnnotations;
using Hastane.Patient.Api.Entities;
using Hastane.Patient.Api.Validation;

namespace Hastane.Patient.Api.Dtos;

// UserId ve UpdatedAt bilerek yok (mass assignment): kimlik token'dan, zaman sunucudan gelir.
// PUT = profilin TAM değiştirilmesi: gönderilmeyen alan boşaltılır.
public class UpdatePatientProfileDto
{
    [BirthDate]
    public DateOnly? DateOfBirth { get; set; }

    [EnumDataType(typeof(Gender))]
    public Gender? Gender { get; set; }

    [EnumDataType(typeof(BloodType))]
    public BloodType? BloodType { get; set; }

    [StringLength(20), RegularExpression(PhonePattern, ErrorMessage = "Telefon yalnız rakam, boşluk, +, -, ( ) içermeli ve 7-20 karakter olmalı.")]
    public string? Phone { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(60)]
    public string? City { get; set; }

    [StringLength(100)]
    public string? EmergencyContactName { get; set; }

    [StringLength(20), RegularExpression(PhonePattern, ErrorMessage = "Telefon yalnız rakam, boşluk, +, -, ( ) içermeli ve 7-20 karakter olmalı.")]
    public string? EmergencyContactPhone { get; set; }

    public const string PhonePattern = @"^\+?[0-9 ()\-]{7,20}$";
}
