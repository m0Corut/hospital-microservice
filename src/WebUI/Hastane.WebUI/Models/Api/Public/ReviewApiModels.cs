namespace Hastane.WebUI.Models.Api.Public;

public sealed class ReviewApiModel
{
    public string Id { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string ReviewerName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class DoctorRatingSummaryApiModel
{
    public string DoctorId { get; set; } = string.Empty;
    public int ReviewCount { get; set; }
    public double AverageRating { get; set; }
    public int RecommendationPercent { get; set; }
}

// Gövdeler: kimlik alanları (ReviewerId/Name) yok, sunucu token'dan alır.
public sealed record CreateReviewRequest(string DoctorId, int Rating, string? Comment);

public sealed record UpdateReviewRequest(string Id, int Rating, string? Comment);
