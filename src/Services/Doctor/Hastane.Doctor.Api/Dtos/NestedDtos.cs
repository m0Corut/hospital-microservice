using System.ComponentModel.DataAnnotations;
using Hastane.Doctor.Api.Validation;

namespace Hastane.Doctor.Api.Dtos;

public class EducationDto
{
    [Required]
    public string School { get; set; } = string.Empty;
    [Required]
    public string Department { get; set; } = string.Empty;
    public int StartYear { get; set; }
    public int EndYear { get; set; }
}

public class ExperienceDto
{
    [Required]
    public string Place { get; set; } = string.Empty;
    [Required]
    public string Position { get; set; } = string.Empty;
    public int StartYear { get; set; }
    public int? EndYear { get; set; }
}

public class AwardDto
{
    [Required]
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public string? Description { get; set; }
}

public class ClinicLocationDto : IValidatableObject
{
    [Required]
    public string ClinicName { get; set; } = string.Empty;
    [Required]
    public string City { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? Address { get; set; }
    public string? Floor { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? HourlyFee { get; set; }

    [Required]
    [RegularExpression(DoctorRules.CurrencyPattern, ErrorMessage = "Currency 3 harfli büyük harf kod olmalı (ör. TRY).")]
    public string Currency { get; set; } = "TRY";

    public List<ScheduleSlotDto> Schedule { get; set; } = [];

    // DTO katmanı savunması: program kuralları (biçim, bitiş > başlangıç, çakışma, <= 21).
    // Servis katmanı aynı DoctorRules'u yeniden çalıştırır (DoctorInputValidator).
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => DoctorRules.ValidateSchedule(Schedule).Select(m => new ValidationResult(m, [nameof(Schedule)]));
}

public class ScheduleSlotDto
{
    public DayOfWeek DayOfWeek { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public int SlotMinutes { get; set; } = 30;
}

public class SocialLinksDto
{
    [OptionalHttpUrl(DoctorRules.MaxSocialLength)] public string? Facebook { get; set; }
    [OptionalHttpUrl(DoctorRules.MaxSocialLength)] public string? X { get; set; }
    [OptionalHttpUrl(DoctorRules.MaxSocialLength)] public string? Instagram { get; set; }
    [OptionalHttpUrl(DoctorRules.MaxSocialLength)] public string? LinkedIn { get; set; }
    [OptionalHttpUrl(DoctorRules.MaxSocialLength)] public string? Pinterest { get; set; }
    [OptionalHttpUrl(DoctorRules.MaxSocialLength)] public string? YouTube { get; set; }
}
