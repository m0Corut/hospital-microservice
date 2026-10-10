using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Patient;

namespace Hastane.WebUI.Clients;

public interface IPatientAppointmentClient
{
    // Hasta için kapsamı sunucu token'dan belirler (patientId gönderilmez, gönderilse de ezilir).
    Task<ApiResult<List<AppointmentApiModel>>> ListAsync(string? status, DateTime? fromUtc, ApiFailures handle, CancellationToken ct);

    Task<ApiResult<AppointmentDetailApiModel>> GetAsync(Guid id, CancellationToken ct);

    Task<ApiResult<AppointmentApiModel>> CreateAsync(CreateAppointmentRequest request, CancellationToken ct);

    Task<ApiResult> SetStatusAsync(Guid id, string status, CancellationToken ct);
}

public sealed class PatientAppointmentClient(IGatewayClient gateway) : IPatientAppointmentClient
{
    public Task<ApiResult<List<AppointmentApiModel>>> ListAsync(string? status, DateTime? fromUtc, ApiFailures handle, CancellationToken ct)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(status)) query.Add("status=" + Uri.EscapeDataString(status));
        if (fromUtc is { } from) query.Add("from=" + Uri.EscapeDataString((from.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(from, DateTimeKind.Utc) : from.ToUniversalTime()).ToString("yyyy-MM-ddTHH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)));
        var path = query.Count == 0 ? "/gateway/appointments" : "/gateway/appointments?" + string.Join('&', query);
        return gateway.GetAsync<List<AppointmentApiModel>>(path, handle, ct);
    }

    public Task<ApiResult<AppointmentDetailApiModel>> GetAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<AppointmentDetailApiModel>($"/gateway/appointments/{id}", ct: ct);

    public Task<ApiResult<AppointmentApiModel>> CreateAsync(CreateAppointmentRequest request, CancellationToken ct)
        // Unavailable ele alınır: doktor servisi kapalıysa API 503 verir, ekran bunu form mesajı yapar.
        => gateway.PostAsync<AppointmentApiModel>("/gateway/appointments", request, ApiFailures.Unavailable, ct);

    // Durum değişikliği yalnız PATCH {id}/status; geçiş kuralları ve "hasta yalnız iptal edebilir" kuralı API'de.
    public Task<ApiResult> SetStatusAsync(Guid id, string status, CancellationToken ct)
        => gateway.PatchAsync($"/gateway/appointments/{id}/status", new UpdateAppointmentStatusRequest(status), ct: ct);
}
