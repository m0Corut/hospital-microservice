namespace Hastane.Review.Api.Dtos;

public class DoctorRatingSummaryDto
{
    public string DoctorId { get; set; } = string.Empty;
    public int ReviewCount { get; set; }
    // 1 ondalık; yorum yoksa 0.
    public double AverageRating { get; set; }
}
