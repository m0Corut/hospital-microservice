using System.ComponentModel.DataAnnotations;

namespace Hastane.Review.Api.Dtos;

// Id ve CreatedAt bilerek yok: ikisini de sunucu atar, istemci gönderemez.
public class CreateReviewDto
{
    [Required]
    public string DoctorId { get; set; } = string.Empty;
    [Required]
    public string ReviewerName { get; set; } = string.Empty;
    [Range(1, 5)]
    public int Rating { get; set; }
    [MaxLength(1000)]
    public string? Comment { get; set; }
}
