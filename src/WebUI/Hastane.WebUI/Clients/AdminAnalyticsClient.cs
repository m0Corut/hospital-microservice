using System.Globalization;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Analytics;

namespace Hastane.WebUI.Clients;

// Analytics servisi (Kafka tüketicisi), yalnız Admin. Servis ya da Kafka kapalıyken sayfa açılsın diye çağrılar
// 403/404/503'ü fırlatmaz, sonuç olarak döner (401 yine girişe yönlendirir).
public interface IAdminAnalyticsClient
{
    // GET analytics/doctors/top-viewed?days=&take= -> [{ doctorId, views }]
    Task<ApiResult<List<TopViewedDoctorApiModel>>> TopViewedAsync(int days, int take, CancellationToken ct);

    // GET analytics/search/top-terms?days=&take= -> [{ term, count }]
    Task<ApiResult<List<TopSearchTermApiModel>>> TopTermsAsync(int days, int take, CancellationToken ct);

    // GET analytics/status -> { kafkaEnabled, kafkaConnected, topic, groupId, lastProcessedAt, processedToday }
    Task<ApiResult<AnalyticsStatusApiModel>> StatusAsync(CancellationToken ct);

    // POST analytics/rebuild -> 200 { partitionsRewound, aggregatesDeleted } | 409 (Kafka kapalı / bağlı değil / sürüyor) | 503
    Task<ApiResult<AnalyticsRebuildApiModel>> RebuildAsync(CancellationToken ct);
}

public sealed class AdminAnalyticsClient(IGatewayClient gateway) : IAdminAnalyticsClient
{
    private const ApiFailures Soft = ApiFailures.Forbidden | ApiFailures.NotFound | ApiFailures.Unavailable;

    public Task<ApiResult<List<TopViewedDoctorApiModel>>> TopViewedAsync(int days, int take, CancellationToken ct)
        => gateway.GetAsync<List<TopViewedDoctorApiModel>>(Path("doctors/top-viewed", days, take), Soft, ct);

    public Task<ApiResult<List<TopSearchTermApiModel>>> TopTermsAsync(int days, int take, CancellationToken ct)
        => gateway.GetAsync<List<TopSearchTermApiModel>>(Path("search/top-terms", days, take), Soft, ct);

    public Task<ApiResult<AnalyticsStatusApiModel>> StatusAsync(CancellationToken ct)
        => gateway.GetAsync<AnalyticsStatusApiModel>("/gateway/analytics/status", Soft, ct);

    public Task<ApiResult<AnalyticsRebuildApiModel>> RebuildAsync(CancellationToken ct)
        => gateway.PostAsync<AnalyticsRebuildApiModel>("/gateway/analytics/rebuild", null, ApiFailures.Forbidden | ApiFailures.NotFound | ApiFailures.Unavailable, ct);

    private static string Path(string tail, int days, int take)
        => string.Create(CultureInfo.InvariantCulture, $"/gateway/analytics/{tail}?days={days}&take={take}");
}
