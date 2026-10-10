using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;

namespace Hastane.WebUI.Clients;

public interface IAdminPrescriptionClient
{
    Task<ApiResult<List<AdminPrescriptionListItemApiModel>>> GetAllAsync(string? patientId, string? doctorId, string? appointmentId, CancellationToken ct);

    Task<ApiResult<AdminPrescriptionApiModel>> GetAsync(Guid id, CancellationToken ct);
}

public sealed class AdminPrescriptionClient(IGatewayClient gateway) : IAdminPrescriptionClient
{
    public Task<ApiResult<List<AdminPrescriptionListItemApiModel>>> GetAllAsync(string? patientId, string? doctorId, string? appointmentId, CancellationToken ct)
        => gateway.GetAsync<List<AdminPrescriptionListItemApiModel>>(
            AdminQuery.Build("/gateway/prescriptions",
                ("patientId", patientId), ("doctorId", doctorId), ("appointmentId", appointmentId)),
            ct: ct);

    public Task<ApiResult<AdminPrescriptionApiModel>> GetAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<AdminPrescriptionApiModel>($"/gateway/prescriptions/{id}", ct: ct);
}
