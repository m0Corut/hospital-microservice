namespace Hastane.WebUI.Models.Api.Admin;

// Review servisi: GET /gateway/reviews (yeniden eskiye). ReviewerId yanıtta yoktur; IsMine Admin için anlamsızdır.
public sealed class AdminReviewApiModel
{
    public string Id { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string ReviewerName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}
