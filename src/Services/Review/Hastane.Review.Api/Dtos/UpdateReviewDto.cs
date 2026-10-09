using System.ComponentModel.DataAnnotations;

namespace Hastane.Review.Api.Dtos;

public class UpdateReviewDto
{
    [Required]
    public string Id { get; set; } = string.Empty;
    // DoctorId, CreatedAt ve yorum sahibi bilgisi bilerek yok: güncellemede değişmezler, gövdede gelirse yok sayılır.
    [Range(1, 5)]
    public int Rating { get; set; }
    [MaxLength(1000)]
    public string? Comment { get; set; }
}
