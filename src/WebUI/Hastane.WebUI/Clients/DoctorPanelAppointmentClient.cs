using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.DoctorPanel;

namespace Hastane.WebUI.Clients;

public interface IDoctorPanelAppointmentClient
{
    Task<ApiResult<List<DoctorPanelAppointmentApiModel>>> ListAsync(
        string doctorId, DoctorPanelAppointmentStatus? status, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct);

    // Hasta profili: appointments?doctorId=&patientId= (ikisi de servisin desteklediği süzgeç; doktor için doctorId zaten token'dan zorlanır).
    Task<ApiResult<List<DoctorPanelAppointmentApiModel>>> ListForPatientAsync(string doctorId, string patientId, CancellationToken ct);

    Task<ApiResult<DoctorPanelAppointmentApiModel>> GetAsync(Guid id, CancellationToken ct);

    Task<ApiResult> SetStatusAsync(Guid id, DoctorPanelAppointmentStatus status, CancellationToken ct);

    Task<ApiResult> SaveDetailAsync(Guid id, string complaint, string? diagnosis, string? doctorNotes, CancellationToken ct);
}

public sealed class DoctorPanelAppointmentClient(IGatewayClient gateway) : IDoctorPanelAppointmentClient
{
    public Task<ApiResult<List<DoctorPanelAppointmentApiModel>>> ListAsync(
        string doctorId, DoctorPanelAppointmentStatus? status, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        var query = new List<string> { $"doctorId={Uri.EscapeDataString(doctorId)}" };
        if (status is not null) query.Add($"status={status}");
        if (fromUtc is not null) query.Add($"from={Uri.EscapeDataString(fromUtc.Value.ToString("O"))}");
        if (toUtc is not null) query.Add($"to={Uri.EscapeDataString(toUtc.Value.ToString("O"))}");
        return gateway.GetAsync<List<DoctorPanelAppointmentApiModel>>($"/gateway/appointments?{string.Join('&', query)}", ct: ct);
    }

    public Task<ApiResult<List<DoctorPanelAppointmentApiModel>>> ListForPatientAsync(string doctorId, string patientId, CancellationToken ct)
        => gateway.GetAsync<List<DoctorPanelAppointmentApiModel>>(
            $"/gateway/appointments?doctorId={Uri.EscapeDataString(doctorId)}&patientId={Uri.EscapeDataString(patientId)}", ct: ct);

    public Task<ApiResult<DoctorPanelAppointmentApiModel>> GetAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<DoctorPanelAppointmentApiModel>($"/gateway/appointments/{id}", ct: ct);

    public Task<ApiResult> SetStatusAsync(Guid id, DoctorPanelAppointmentStatus status, CancellationToken ct)
        => gateway.PatchAsync($"/gateway/appointments/{id}/status", new { status }, ct: ct);

    public Task<ApiResult> SaveDetailAsync(Guid id, string complaint, string? diagnosis, string? doctorNotes, CancellationToken ct)
        => gateway.PutAsync($"/gateway/appointments/{id}/detail", new { complaint, diagnosis, doctorNotes }, ct: ct);
}
