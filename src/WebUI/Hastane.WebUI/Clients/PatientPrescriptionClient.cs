using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Patient;

namespace Hastane.WebUI.Clients;

public interface IPatientPrescriptionClient
{
    // En yeni üstte; hasta için kapsamı sunucu token'dan belirler.
    Task<ApiResult<List<PrescriptionApiModel>>> ListAsync(ApiFailures handle, CancellationToken ct);

    Task<ApiResult<PrescriptionDetailApiModel>> GetAsync(Guid id, CancellationToken ct);
}

public sealed class PatientPrescriptionClient(IGatewayClient gateway) : IPatientPrescriptionClient
{
    public Task<ApiResult<List<PrescriptionApiModel>>> ListAsync(ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<List<PrescriptionApiModel>>("/gateway/prescriptions", handle, ct);

    public Task<ApiResult<PrescriptionDetailApiModel>> GetAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<PrescriptionDetailApiModel>($"/gateway/prescriptions/{id}", ct: ct);
}
