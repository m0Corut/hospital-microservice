using System.ComponentModel.DataAnnotations;

namespace Hastane.Review.Api.Dtos;

// Id, CreatedAt, ReviewerId ve ReviewerName bilerek yok: sunucu atar (kimlik token'dan), istemci gönderemez.
public class CreateReviewDto
{
    [Required]
    public string DoctorId { get; set; } = string.Empty;
    [Range(1, 5)]
    public int Rating { get; set; }
    [MaxLength(1000)]
    public string? Comment { get; set; }
}
