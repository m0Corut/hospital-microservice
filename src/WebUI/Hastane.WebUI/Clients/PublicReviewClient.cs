using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Public;

namespace Hastane.WebUI.Clients;

public interface IPublicReviewClient
{
    // Toplu puan özeti (doktor kartları): tek çağrı, N+1 yok. En çok 100 id.
    Task<ApiResult<List<DoctorRatingSummaryApiModel>>> GetSummariesAsync(IEnumerable<string> doctorIds, ApiFailures handle, CancellationToken ct);

    Task<ApiResult<DoctorRatingSummaryApiModel>> GetSummaryAsync(string doctorId, ApiFailures handle, CancellationToken ct);

    Task<ApiResult<List<ReviewApiModel>>> GetByDoctorAsync(string doctorId, ApiFailures handle, CancellationToken ct);

    Task<ApiResult<ReviewApiModel>> CreateAsync(CreateReviewRequest request, CancellationToken ct);

    Task<ApiResult> UpdateAsync(UpdateReviewRequest request, CancellationToken ct);

    Task<ApiResult> DeleteAsync(string id, CancellationToken ct);
}

public sealed class PublicReviewClient(IGatewayClient gateway) : IPublicReviewClient
{
    public Task<ApiResult<List<DoctorRatingSummaryApiModel>>> GetSummariesAsync(IEnumerable<string> doctorIds, ApiFailures handle, CancellationToken ct)
    {
        var joined = string.Join(',', doctorIds.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().Take(100));
        return gateway.GetAsync<List<DoctorRatingSummaryApiModel>>("/gateway/reviews/summaries?doctorIds=" + Uri.EscapeDataString(joined), handle, ct);
    }

    public Task<ApiResult<DoctorRatingSummaryApiModel>> GetSummaryAsync(string doctorId, ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<DoctorRatingSummaryApiModel>($"/gateway/reviews/doctors/{Uri.EscapeDataString(doctorId)}/summary", handle, ct);

    public Task<ApiResult<List<ReviewApiModel>>> GetByDoctorAsync(string doctorId, ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<List<ReviewApiModel>>("/gateway/reviews?doctorId=" + Uri.EscapeDataString(doctorId), handle, ct);

    public Task<ApiResult<ReviewApiModel>> CreateAsync(CreateReviewRequest request, CancellationToken ct)
        => gateway.PostAsync<ReviewApiModel>("/gateway/reviews", request, ct: ct);

    public Task<ApiResult> UpdateAsync(UpdateReviewRequest request, CancellationToken ct)
        => gateway.PutAsync($"/gateway/reviews/{Uri.EscapeDataString(request.Id)}", request, ApiFailures.NotFound, ct);

    public Task<ApiResult> DeleteAsync(string id, CancellationToken ct)
        => gateway.DeleteAsync($"/gateway/reviews/{Uri.EscapeDataString(id)}", ApiFailures.NotFound, ct);
}
