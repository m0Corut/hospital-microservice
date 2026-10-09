using System.ComponentModel.DataAnnotations;

namespace Hastane.Review.Api.Dtos;

public class UpdateReviewDto
{
    [Required]
    public string Id { get; set; } = string.Empty;
    // DoctorId ve CreatedAt bilerek yok: güncellemede değişmezler, gövdede gelirse yok sayılır.
    [Required]
    public string ReviewerName { get; set; } = string.Empty;
    [Range(1, 5)]
    public int Rating { get; set; }
    [MaxLength(1000)]
    public string? Comment { get; set; }
}
