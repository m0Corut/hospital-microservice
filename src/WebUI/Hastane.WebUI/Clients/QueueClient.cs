using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Queue;

namespace Hastane.WebUI.Clients;

public interface IQueueClient
{
    // Hasta: sıraya gir. Yeni kayıt 201, aynı randevu ikinci kez 200; 409 (onaysız / bugün değil) ve 503 ekranda gösterilir.
    Task<ApiResult<QueueEntryApiModel>> CheckInAsync(Guid appointmentId, CancellationToken ct);

    Task<ApiResult<List<QueueEntryApiModel>>> MineTodayAsync(CancellationToken ct);

    // Doktor: kendi bugünkü sırası (doktor kimliği token'dan).
    Task<ApiResult<List<QueueEntryApiModel>>> DoctorTodayAsync(CancellationToken ct);

    // Doktor eylemleri: 409 (zaten çağrılmış kişi var, durum uymuyor) ekranda uyarı olur, sayfa düşmez.
    Task<ApiResult<QueueEntryApiModel>> CallNextAsync(CancellationToken ct);

    Task<ApiResult<QueueEntryApiModel>> CallAsync(Guid id, CancellationToken ct);

    Task<ApiResult<QueueEntryApiModel>> CompleteAsync(Guid id, CancellationToken ct);

    Task<ApiResult<QueueEntryApiModel>> NoShowAsync(Guid id, CancellationToken ct);

    // Anonim çağrı ekranı verisi. Servis kapalıysa ekran düşmesin: hata ApiResult olarak döner.
    Task<ApiResult<QueueDisplayApiModel>> DisplayAsync(string doctorId, CancellationToken ct);
}

public sealed class QueueClient(IGatewayClient gateway) : IQueueClient
{
    public Task<ApiResult<QueueEntryApiModel>> CheckInAsync(Guid appointmentId, CancellationToken ct)
        // Unavailable ele alınır: randevu servisi kapalıysa Queue 503 verir, ekran bunu uyarıya çevirir.
        => gateway.PostAsync<QueueEntryApiModel>("/gateway/queue/check-in", new QueueCheckInRequest(appointmentId), ApiFailures.Unavailable, ct);

    public Task<ApiResult<List<QueueEntryApiModel>>> MineTodayAsync(CancellationToken ct)
        => gateway.GetAsync<List<QueueEntryApiModel>>("/gateway/queue/me/today", ApiFailures.Unavailable, ct);

    public Task<ApiResult<List<QueueEntryApiModel>>> DoctorTodayAsync(CancellationToken ct)
        => gateway.GetAsync<List<QueueEntryApiModel>>("/gateway/queue/doctor/today", ApiFailures.Unavailable, ct);

    public Task<ApiResult<QueueEntryApiModel>> CallNextAsync(CancellationToken ct)
        => gateway.PostAsync<QueueEntryApiModel>("/gateway/queue/doctor/call-next", null, ApiFailures.Unavailable, ct);

    public Task<ApiResult<QueueEntryApiModel>> CallAsync(Guid id, CancellationToken ct)
        => gateway.PostAsync<QueueEntryApiModel>($"/gateway/queue/{id}/call", null, ApiFailures.Unavailable, ct);

    public Task<ApiResult<QueueEntryApiModel>> CompleteAsync(Guid id, CancellationToken ct)
        => gateway.PostAsync<QueueEntryApiModel>($"/gateway/queue/{id}/complete", null, ApiFailures.Unavailable, ct);

    public Task<ApiResult<QueueEntryApiModel>> NoShowAsync(Guid id, CancellationToken ct)
        => gateway.PostAsync<QueueEntryApiModel>($"/gateway/queue/{id}/no-show", null, ApiFailures.Unavailable, ct);

    public Task<ApiResult<QueueDisplayApiModel>> DisplayAsync(string doctorId, CancellationToken ct)
        => gateway.GetAsync<QueueDisplayApiModel>($"/gateway/queue/display?doctorId={Uri.EscapeDataString(doctorId)}", ApiFailures.All, ct);
}
