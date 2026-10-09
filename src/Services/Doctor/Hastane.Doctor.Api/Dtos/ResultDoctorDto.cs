using Hastane.Doctor.Api.Entities;

namespace Hastane.Doctor.Api.Dtos;

// Liste görünümü: kısa ve herkese açık alanlar. Phone/Email bilerek YOK (yalnız GetById'de).
public class ResultDoctorDto
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Degrees { get; set; }
    public DoctorGender Gender { get; set; }
    public string BranchId { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }

    // Türetilmiş (saklanmaz).
    public string? PrimaryCity { get; set; }
    public decimal? MinFee { get; set; }
    public decimal? MaxFee { get; set; }
    public string? Currency { get; set; }
}
