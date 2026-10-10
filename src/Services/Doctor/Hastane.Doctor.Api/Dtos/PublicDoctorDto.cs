using Hastane.Doctor.Api.Entities;

namespace Hastane.Doctor.Api.Dtos;

// Anonim ziyaretçiye açık profil. GetByIdDoctorDto'nun kopyası DEĞİL: Phone ve Email bilerek YOK.
// Ayrı sınıf olduğu için yarın biri GetByIdDoctorDto'ya yeni bir gizli alan eklese bile buraya kendiliğinden sızmaz
// (alanlar burada tek tek, bilerek yazılır).
public class PublicDoctorDto
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public DoctorGender Gender { get; set; }
    public string? Degrees { get; set; }
    public string BranchId { get; set; } = string.Empty;
    public string? Biography { get; set; }
    public string? ImageUrl { get; set; }
    public List<string> Services { get; set; } = [];
    public List<string> Gallery { get; set; } = [];
    public SocialLinksDto SocialLinks { get; set; } = new();
    public List<EducationDto> Educations { get; set; } = [];
    public List<ExperienceDto> Experiences { get; set; } = [];
    public List<AwardDto> Awards { get; set; } = [];
    public List<ClinicLocationDto> ClinicLocations { get; set; } = [];

    // Türetilmiş (saklanmaz).
    public string? PrimaryCity { get; set; }
    public decimal? MinFee { get; set; }
    public decimal? MaxFee { get; set; }
    public string? Currency { get; set; }
}
