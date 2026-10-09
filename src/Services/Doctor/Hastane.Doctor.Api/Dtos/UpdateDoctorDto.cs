using System.ComponentModel.DataAnnotations;

namespace Hastane.Doctor.Api.Dtos;

public class UpdateDoctorDto
{
    [Required]
    public string Id { get; set; } = string.Empty;
    [Required]
    public string FirstName { get; set; } = string.Empty;
    [Required]
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }
    [Required]
    public string BranchId { get; set; } = string.Empty;
    public string? Biography { get; set; }
    public string? ImageUrl { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public List<EducationDto> Educations { get; set; } = [];
    public List<ExperienceDto> Experiences { get; set; } = [];
    public List<AwardDto> Awards { get; set; } = [];
    public List<ClinicLocationDto> ClinicLocations { get; set; } = [];
}
