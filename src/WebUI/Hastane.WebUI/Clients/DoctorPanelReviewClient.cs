using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.DoctorPanel;

namespace Hastane.WebUI.Clients;

public interface IDoctorPanelReviewClient
{
    Task<ApiResult<List<DoctorPanelReviewApiModel>>> ListAsync(string doctorId, CancellationToken ct);

    Task<ApiResult<DoctorPanelRatingSummaryApiModel>> SummaryAsync(string doctorId, CancellationToken ct);
}

public sealed class DoctorPanelReviewClient(IGatewayClient gateway) : IDoctorPanelReviewClient
{
    public Task<ApiResult<List<DoctorPanelReviewApiModel>>> ListAsync(string doctorId, CancellationToken ct)
        => gateway.GetAsync<List<DoctorPanelReviewApiModel>>($"/gateway/reviews?doctorId={Uri.EscapeDataString(doctorId)}", ct: ct);

    public Task<ApiResult<DoctorPanelRatingSummaryApiModel>> SummaryAsync(string doctorId, CancellationToken ct)
        => gateway.GetAsync<DoctorPanelRatingSummaryApiModel>($"/gateway/reviews/doctors/{Uri.EscapeDataString(doctorId)}/summary", ct: ct);
}
