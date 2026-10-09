namespace Hastane.Review.Api.Dtos;

public class DoctorRatingSummaryDto
{
    public string DoctorId { get; set; } = string.Empty;
    public int ReviewCount { get; set; }
    // 1 ondalık; yorum yoksa 0.
    public double AverageRating { get; set; }
    // Puanı >= 4 olan yorumların yüzdesi, en yakın tam sayıya (0.5 yukarı); yorum yoksa 0. Karttaki "%98".
    public int RecommendationPercent { get; set; }
}
