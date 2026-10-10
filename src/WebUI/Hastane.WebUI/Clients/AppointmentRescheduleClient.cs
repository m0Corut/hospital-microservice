using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Patient;

namespace Hastane.WebUI.Clients;

// Yeniden planlama (PUT appointments/{id}): hasta, doktor ve yönetici AYNI uçu kullanır, kimin yapabildiğini API belirler
// (randevunun hastası, doktoru ya da Admin; başkası 404). Gövdede yalnız şube ve tarih var; doktor, hasta ve durum değişmez.
public interface IAppointmentRescheduleClient
{
    Task<ApiResult<AppointmentApiModel>> RescheduleAsync(Guid id, string branchId, DateTime appointmentUtc, CancellationToken ct);
}

public sealed class AppointmentRescheduleClient(IGatewayClient gateway) : IAppointmentRescheduleClient
{
    public Task<ApiResult<AppointmentApiModel>> RescheduleAsync(Guid id, string branchId, DateTime appointmentUtc, CancellationToken ct)
        => gateway.PutAsync<AppointmentApiModel>($"/gateway/appointments/{id}", new { id, branchId, appointmentDate = appointmentUtc }, ct: ct);
}
