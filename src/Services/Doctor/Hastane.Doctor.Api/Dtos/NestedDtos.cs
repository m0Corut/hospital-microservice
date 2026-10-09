using System.ComponentModel.DataAnnotations;

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

public class ClinicLocationDto
{
    [Required]
    public string ClinicName { get; set; } = string.Empty;
    [Required]
    public string City { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? Address { get; set; }
    public string? Floor { get; set; }
}
