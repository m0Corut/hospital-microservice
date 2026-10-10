namespace Hastane.Review.Api.Dtos;

public class ResultReviewDto
{
    public string Id { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string ReviewerName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }

    // "Bu yorum benim mi?": istek token'lıysa ve ReviewerId == sub ise true; anonimde / başkasının yorumunda false.
    // ReviewerId'nin kendisi bilerek yanıtta YOK; istemci yalnız kendi yorumunu ayırt eder, başkasının kimliğini öğrenmez.
    public bool IsMine { get; set; }
}
