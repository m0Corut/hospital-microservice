using System.Globalization;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Patient;

namespace Hastane.WebUI.Clients;

// Doktorun belirli bir gün için boş randevu slotları (GET appointments/availability). Hasta ve doktor paneli ortak kullanır:
// slot hesabı (doktor programı - dolu randevular - geçmiş saatler) API'dedir, WebUI yalnız gösterir.
public interface IAppointmentAvailabilityClient
{
    // date: kliniğin yerel günü (İstanbul takvimi). Doktor servisi kapalıysa API 503 verir: handle ile ekranda ele alınır.
    Task<ApiResult<List<AvailableSlotApiModel>>> GetAsync(string doctorId, DateOnly date, ApiFailures handle, CancellationToken ct);
}

public sealed class AppointmentAvailabilityClient(IGatewayClient gateway) : IAppointmentAvailabilityClient
{
    public Task<ApiResult<List<AvailableSlotApiModel>>> GetAsync(string doctorId, DateOnly date, ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<List<AvailableSlotApiModel>>(
            $"/gateway/appointments/availability?doctorId={Uri.EscapeDataString(doctorId)}&date={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            handle, ct);
}
