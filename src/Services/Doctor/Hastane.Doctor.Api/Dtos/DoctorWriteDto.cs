using System.ComponentModel.DataAnnotations;
using Hastane.Doctor.Api.Entities;
using Hastane.Doctor.Api.Validation;

namespace Hastane.Doctor.Api.Dtos;

// Create ve Update ortak alanları (tek yerde: yeni alan eklenince iki DTO ayrışmaz).
public abstract class DoctorWriteDto
{
    [Required]
    public string FirstName { get; set; } = string.Empty;
    [Required]
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }

    // Nullable: gönderilmezse null kalır ve [Required] yakalar. Nullable olmasaydı eksik alan sessizce Male (0) olurdu.
    [Required]
    public DoctorGender? Gender { get; set; }

    [MaxLength(DoctorRules.MaxDegrees)]
    public string? Degrees { get; set; }

    [Required]
    public string BranchId { get; set; } = string.Empty;
    public string? Biography { get; set; }
    public string? ImageUrl { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    [MaxLength(DoctorRules.MaxServices)]
    [EachMaxLength(DoctorRules.MaxServiceLength)]
    public List<string> Services { get; set; } = [];

    [MaxLength(DoctorRules.MaxGallery)]
    [EachHttpUrl(DoctorRules.MaxUrlLength)]
    public List<string> Gallery { get; set; } = [];

    public SocialLinksDto SocialLinks { get; set; } = new();

    public List<EducationDto> Educations { get; set; } = [];
    public List<ExperienceDto> Experiences { get; set; } = [];
    public List<AwardDto> Awards { get; set; } = [];
    public List<ClinicLocationDto> ClinicLocations { get; set; } = [];
}
