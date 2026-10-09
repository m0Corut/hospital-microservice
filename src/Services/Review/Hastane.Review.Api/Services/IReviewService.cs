using Hastane.Review.Api.Dtos;

namespace Hastane.Review.Api.Services;

public interface IReviewService
{
    Task<List<ResultReviewDto>> GetAllAsync(CancellationToken ct);
    /// <summary>Yeniden eskiye (CreatedAt azalan).</summary>
    Task<List<ResultReviewDto>> GetByDoctorIdAsync(string doctorId, CancellationToken ct);
    Task<ResultReviewDto?> GetByIdAsync(string id, CancellationToken ct);
    Task<ResultReviewDto> CreateAsync(CreateReviewDto dto, CancellationToken ct);
    /// <summary>Kayıt yoksa false döner. DoctorId ve CreatedAt değişmez.</summary>
    Task<bool> UpdateAsync(UpdateReviewDto dto, CancellationToken ct);
    /// <summary>Kayıt yoksa false döner.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct);
    /// <summary>Yorumu olmayan doktor için ReviewCount=0, AverageRating=0.</summary>
    Task<DoctorRatingSummaryDto> GetDoctorSummaryAsync(string doctorId, CancellationToken ct);
}
