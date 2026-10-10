using Hastane.Review.Api.Dtos;

namespace Hastane.Review.Api.Services;

public interface IReviewService
{
    /// <summary>viewerId: isteği yapanın token "sub"ı (anonimde null); yalnız IsMine hesabında kullanılır.</summary>
    Task<List<ResultReviewDto>> GetAllAsync(string? viewerId, CancellationToken ct);
    /// <summary>Yeniden eskiye (CreatedAt azalan).</summary>
    Task<List<ResultReviewDto>> GetByDoctorIdAsync(string doctorId, string? viewerId, CancellationToken ct);
    Task<ResultReviewDto?> GetByIdAsync(string id, string? viewerId, CancellationToken ct);
    /// <summary>Yorum sahibi (reviewerId, reviewerName) çağıranın token'ından gelir, DTO'dan değil.</summary>
    Task<ResultReviewDto> CreateAsync(CreateReviewDto dto, string reviewerId, string reviewerName, CancellationToken ct);
    /// <summary>
    /// Kayıt yoksa false döner. DoctorId, CreatedAt ve yorum sahibi değişmez.
    /// ownerId null ise sahiplik aranmaz (Admin); doluysa yalnız ReviewerId == ownerId olan yorum güncellenir
    /// (başkasının ya da ReviewerId'si olmayan yorum "yok" sayılır, false).
    /// </summary>
    Task<bool> UpdateAsync(UpdateReviewDto dto, string? ownerId, CancellationToken ct);
    /// <summary>Kayıt yoksa false döner. ownerId anlamı UpdateAsync ile aynı.</summary>
    Task<bool> DeleteAsync(string id, string? ownerId, CancellationToken ct);
    /// <summary>Yorumu olmayan doktor için ReviewCount=0, AverageRating=0, RecommendationPercent=0.</summary>
    Task<DoctorRatingSummaryDto> GetDoctorSummaryAsync(string doctorId, CancellationToken ct);
    /// <summary>
    /// İstenen HER kimlik için bir kayıt, istenen sırayla (yorumsuz doktor için sıfırlı kayıt). Tek aggregation.
    /// Çağıran kimlikleri temizlemiş olmalı (tekrarsız, boşsuz).
    /// </summary>
    Task<List<DoctorRatingSummaryDto>> GetDoctorSummariesAsync(IReadOnlyList<string> doctorIds, CancellationToken ct);
}
