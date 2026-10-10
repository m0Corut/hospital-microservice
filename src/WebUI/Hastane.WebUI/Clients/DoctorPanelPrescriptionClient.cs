using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.DoctorPanel;

namespace Hastane.WebUI.Clients;

public sealed record DoctorPanelPrescriptionItemBody(string MedicineName, string Dosage, string Frequency, int DurationDays, string? Instructions);

public interface IDoctorPanelPrescriptionClient
{
    Task<ApiResult<List<DoctorPanelPrescriptionSummaryApiModel>>> ListAsync(string doctorId, Guid? appointmentId, CancellationToken ct);

    // Hasta profili: prescriptions?doctorId=&patientId= (doktor kapsamı serviste token'dan zorlanır).
    Task<ApiResult<List<DoctorPanelPrescriptionSummaryApiModel>>> ListForPatientAsync(string doctorId, string patientId, CancellationToken ct);

    Task<ApiResult<DoctorPanelPrescriptionApiModel>> GetAsync(Guid id, CancellationToken ct);

    Task<ApiResult<DoctorPanelPrescriptionApiModel>> CreateAsync(
        Guid appointmentId, string patientId, string? notes, IReadOnlyList<DoctorPanelPrescriptionItemBody> items, CancellationToken ct);

    Task<ApiResult> UpdateAsync(Guid id, string? notes, IReadOnlyList<DoctorPanelPrescriptionItemBody> items, CancellationToken ct);

    Task<ApiResult> DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class DoctorPanelPrescriptionClient(IGatewayClient gateway) : IDoctorPanelPrescriptionClient
{
    public Task<ApiResult<List<DoctorPanelPrescriptionSummaryApiModel>>> ListAsync(string doctorId, Guid? appointmentId, CancellationToken ct)
    {
        var path = $"/gateway/prescriptions?doctorId={Uri.EscapeDataString(doctorId)}";
        if (appointmentId is not null) path += $"&appointmentId={appointmentId}";
        return gateway.GetAsync<List<DoctorPanelPrescriptionSummaryApiModel>>(path, ct: ct);
    }

    public Task<ApiResult<List<DoctorPanelPrescriptionSummaryApiModel>>> ListForPatientAsync(string doctorId, string patientId, CancellationToken ct)
        => gateway.GetAsync<List<DoctorPanelPrescriptionSummaryApiModel>>(
            $"/gateway/prescriptions?doctorId={Uri.EscapeDataString(doctorId)}&patientId={Uri.EscapeDataString(patientId)}", ct: ct);

    public Task<ApiResult<DoctorPanelPrescriptionApiModel>> GetAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<DoctorPanelPrescriptionApiModel>($"/gateway/prescriptions/{id}", ct: ct);

    // DoctorId gönderilmez: servis onu token'daki doctor_id'den alır.
    public Task<ApiResult<DoctorPanelPrescriptionApiModel>> CreateAsync(
        Guid appointmentId, string patientId, string? notes, IReadOnlyList<DoctorPanelPrescriptionItemBody> items, CancellationToken ct)
        => gateway.PostAsync<DoctorPanelPrescriptionApiModel>("/gateway/prescriptions", new { appointmentId, patientId, notes, items }, ct: ct);

    public Task<ApiResult> UpdateAsync(Guid id, string? notes, IReadOnlyList<DoctorPanelPrescriptionItemBody> items, CancellationToken ct)
        => gateway.PutAsync($"/gateway/prescriptions/{id}", new { id, notes, items }, ct: ct);

    public Task<ApiResult> DeleteAsync(Guid id, CancellationToken ct)
        => gateway.DeleteAsync($"/gateway/prescriptions/{id}", ct: ct);
}
